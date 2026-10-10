using System.Globalization;
using System.Text.Json;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Publishing;
using RunesmithHub.Tool.Registry;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Gates;

/// <summary>The outcome of one gate, reported as the commit status <c>hub/&lt;name&gt;</c>.</summary>
internal sealed record GateResult(string Name, CommitStatusState State, IReadOnlyList<string> Lines)
{
    public string Context => "hub/" + Name;

    public string Description => Lines.Count > 0 ? Lines[0] : State == CommitStatusState.Success ? "Passed." : "Waiting.";

    /// <summary>Gets the administrator review that requested changes on a new version, which rejects it.</summary>
    public PullRequestReview? Rejection { get; init; }
}

/// <summary>A pull request and what it changes, compared from the commit it branched from.</summary>
internal sealed record GateInputs(PullRequest Pull, string BaseRef, RegistryChanges Changes);

/// <summary>The three required checks of every registry pull request: verify, review and hold.</summary>
internal sealed class VersionGates(IGitHubReader github, IArtifactSource artifacts, IGitRepository git, TimeProvider time, string? botLogin)
{
    private static readonly TimeSpan CoolingPeriod = TimeSpan.FromHours(24);

    /// <summary>Reads a pull request whose head commit the local repository has, and its changes since its merge base with the default branch.</summary>
    public static async Task<GateInputs> LoadAsync(IGitHubReader github, IGitRepository git, int number, string defaultBranch, CancellationToken cancellationToken)
    {
        var pull = await github.GetPullRequestAsync(HubLocations.Registry, number, cancellationToken).ConfigureAwait(false)
            ?? throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"Pull request #{number} does not exist."));
        var baseRef = await git.MergeBaseAsync(pull.HeadSha, defaultBranch, cancellationToken).ConfigureAwait(false);
        return new GateInputs(pull, baseRef, await RegistryChanges.ReadAsync(git, baseRef, pull.HeadSha, cancellationToken).ConfigureAwait(false));
    }

    public async Task<GateResult> VerifyAsync(GateInputs inputs, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        foreach (var change in inputs.Changes.Versions)
        {
            switch (change)
            {
                case { Before: null, After: { } added }:
                    problems.AddRange(await VerifyNewAsync(change, added, inputs.BaseRef, cancellationToken).ConfigureAwait(false));
                    break;
                case { Before: { } before, After: { } after }:
                    if (!Same(after with { State = before.State, YankReason = before.YankReason }, before))
                        problems.Add($"{change.Path}: only the state and the yank reason of an existing version record may change.");
                    break;
                default:
                    problems.Add($"{change.Path}: version records are never deleted.");
                    break;
            }
        }

        return problems.Count == 0
            ? new GateResult("verify", CommitStatusState.Success, [inputs.Changes.Versions.Count == 0 ? "No version records changed." : "Every version record matches the pipeline's output."])
            : new GateResult("verify", CommitStatusState.Failure, problems);
    }

    public async Task<GateResult> ReviewAsync(GateInputs inputs, CancellationToken cancellationToken)
    {
        var pull = inputs.Pull;
        var admins = await github.GetTeamMemberIdsAsync(HubLocations.Organization, HubLocations.AdminsTeam, cancellationToken).ConfigureAwait(false);
        var reviews = await github.GetReviewsAsync(HubLocations.Registry, pull.Number, cancellationToken).ConfigureAwait(false);
        var comments = await github.GetCommentsAsync(HubLocations.Registry, pull.Number, cancellationToken).ConfigureAwait(false);
        var approvals = Approvals.Of(reviews, comments, pull.HeadSha, botLogin);
        var adminApproved = approvals.Any(approval => admins.Contains(approval.By.Id));
        var byBot = botLogin is not null && string.Equals(pull.Author.Login, botLogin, StringComparison.Ordinal);

        var waiting = new List<string>();
        foreach (var change in inputs.Changes.Versions)
        {
            if (change is { Before: null, After.State: VersionState.Review } && !adminApproved)
            {
                if (Approvals.ChangesRequested(reviews, admins))
                {
                    var rejection = reviews.Where(review => review.State == "CHANGES_REQUESTED" && admins.Contains(review.Author.Id)).MaxBy(review => review.SubmittedAt);
                    return new GateResult("review", CommitStatusState.Failure, [$"An administrator requested changes on {change.PluginId} {change.Version}."]) { Rejection = rejection };
                }

                waiting.Add($"{change.PluginId} {change.Version} needs an administrator's approval.");
            }
            else if (change is { Before: { } before, After: { } after } && before.State != after.State && !adminApproved)
            {
                if (Loosens(before.State, after.State))
                    waiting.Add($"{change.PluginId} {change.Version} goes from {Name(before.State)} to {Name(after.State)}, which needs an administrator's approval.");
                else if (!byBot)
                    waiting.Add($"{change.PluginId} {change.Version} changes state outside the hub's workflows, which needs an administrator's approval.");
            }
        }

        if (inputs.Changes.Strict.Count > 0)
            waiting.AddRange(await StrictPathWaitingAsync(inputs, admins, approvals, cancellationToken).ConfigureAwait(false));

        return waiting.Count == 0
            ? new GateResult("review", CommitStatusState.Success, ["No review is needed, or the needed approvals are there."])
            : new GateResult("review", CommitStatusState.Pending, waiting);
    }

    public GateResult Hold(GateInputs inputs)
    {
        var now = time.GetUtcNow();
        var waiting = inputs.Changes.Versions
            .Where(change => change is { Before: null, After: { State: VersionState.Held, HoldUntil: { } until } } && until > now)
            .Select(change => string.Create(CultureInfo.InvariantCulture, $"{change.PluginId} {change.Version} is held until {change.After!.HoldUntil:yyyy-MM-dd HH:mm} UTC."))
            .ToList();
        return waiting.Count == 0
            ? new GateResult("hold", CommitStatusState.Success, ["No version is in its hold."])
            : new GateResult("hold", CommitStatusState.Pending, waiting);
    }

    private async Task<IEnumerable<string>> VerifyNewAsync(VersionChange change, RegistryVersionRecord added, string baseRef, CancellationToken cancellationToken)
    {
        var folder = Directory.CreateTempSubdirectory("rshub-verify-");
        try
        {
            var analysisFolder = Path.Combine(folder.FullName, "analysis");
            var packageFolder = Path.Combine(folder.FullName, "package");
            await artifacts.DownloadAsync(added.BuildRun, WorkflowArtifacts.Analysis(change.PluginId, change.Version), analysisFolder, cancellationToken).ConfigureAwait(false);
            await artifacts.DownloadAsync(added.BuildRun, WorkflowArtifacts.Package(change.PluginId, change.Version), packageFolder, cancellationToken).ConfigureAwait(false);

            var analysisBytes = await File.ReadAllBytesAsync(Path.Combine(analysisFolder, AnalysisReport.FileName), cancellationToken).ConfigureAwait(false);
            if (!Hashes.Matches(analysisBytes, added.AnalysisDigest))
                return [$"{change.Path}: the analysis of run {added.BuildRun} does not match the record's analysisDigest."];

            var job = BuildJob.Read(Path.Combine(analysisFolder, BuildJob.FileName));
            if (!string.Equals(job.Plugin, change.PluginId, StringComparison.Ordinal) || job.Version != change.Version.ToString())
                return [$"{change.Path}: run {added.BuildRun} built {job.Plugin} {job.Version}."];

            var registry = await new RegistryReader(new GitRegistryFiles(git, baseRef)).LoadAsync(cancellationToken).ConfigureAwait(false);
            var inputs = VersionInputs.Read(analysisBytes, job, packageFolder, added.BuildRun);
            var expected = (await VersionRecordFactory.CreateAsync(inputs, registry, new GitRegistryHistory(git, baseRef), cancellationToken).ConfigureAwait(false)).Record;
            if (Same(added, expected) || expected.State == VersionState.Held && Same(added, expected with { State = VersionState.Review, HoldUntil = null }))
                return [];
            return [$"{change.Path}: differs from the pipeline's output in {string.Join(", ", DifferentFields(added, expected))}."];
        }
        catch (ToolException exception)
        {
            return [$"{change.Path}: {exception.Message}"];
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private async Task<IEnumerable<string>> StrictPathWaitingAsync(GateInputs inputs, IReadOnlySet<long> admins, IReadOnlyList<Approval> approvals, CancellationToken cancellationToken)
    {
        var owners = await github.GetTeamMemberIdsAsync(HubLocations.Organization, HubLocations.OwnersTeam, cancellationToken).ConfigureAwait(false);
        var requesters = inputs.Changes.Strict.Select(change => change.RequestedBy).OfType<long>().ToHashSet();
        var what = string.Join("; ", inputs.Changes.Strict.Select(change => change.Description));
        if (admins.Count >= 2)
        {
            return approvals.Any(approval => owners.Contains(approval.By.Id) && !requesters.Contains(approval.By.Id))
                ? []
                : [$"Strict path ({what}): needs an owner's approval from someone other than who asked."];
        }

        var waiting = new List<string>();
        if (!approvals.Any(approval => owners.Contains(approval.By.Id)))
            waiting.Add($"Strict path ({what}): needs an owner's approval.");
        var cooled = inputs.Pull.CreatedAt + CoolingPeriod;
        if (time.GetUtcNow() < cooled)
            waiting.Add(string.Create(CultureInfo.InvariantCulture, $"Strict path with one administrator: the cooling period ends {cooled:yyyy-MM-dd HH:mm} UTC."));
        return waiting;
    }

    /// <summary>Gets whether a state change makes a version available again.</summary>
    private static bool Loosens(VersionState before, VersionState after) =>
        EffectiveStateRank(after) < EffectiveStateRank(before);

    private static int EffectiveStateRank(VersionState state) => state switch
    {
        VersionState.Published or VersionState.Held => 0,
        VersionState.Review => 1,
        _ => Protocol.Catalog.EffectiveState.Restrictiveness(state),
    };

    private static string Name(VersionState state) => state.ToString().ToLowerInvariant();

    private static bool Same(RegistryVersionRecord left, RegistryVersionRecord right) =>
        RegistryJson.Serialize(left).AsSpan().SequenceEqual(RegistryJson.Serialize(right));

    private static List<string> DifferentFields(RegistryVersionRecord left, RegistryVersionRecord right)
    {
        using var a = JsonDocument.Parse(RegistryJson.Serialize(left));
        using var b = JsonDocument.Parse(RegistryJson.Serialize(right));
        var names = a.RootElement.EnumerateObject().Select(property => property.Name).Union(b.RootElement.EnumerateObject().Select(property => property.Name), StringComparer.Ordinal);
        return names.Where(name =>
            !(a.RootElement.TryGetProperty(name, out var x) && b.RootElement.TryGetProperty(name, out var y) && string.Equals(x.GetRawText(), y.GetRawText(), StringComparison.Ordinal)))
            .ToList();
    }
}
