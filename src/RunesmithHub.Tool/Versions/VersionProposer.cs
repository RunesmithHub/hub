using System.Globalization;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Versions;

/// <summary>What a proposal did.</summary>
/// <param name="PullRequest">The version pull request, or null when the version was rejected.</param>
internal sealed record ProposalResult(RegistryVersionRecord? Record, int? PullRequest, string Message);

/// <summary>Turns an analysis into a version pull request, or rejects the version on its tracking issue.</summary>
internal sealed class VersionProposer(IGitHubWriter bot)
{
    /// <summary>Writes the version record and opens its pull request, or rejects the version when the checks against the hub fail.</summary>
    /// <param name="catalog">The published index, for the dependency checks.</param>
    public async Task<ProposalResult> ProposeAsync(
        VersionInputs inputs, string summary, RegistryState registry, IRegistryHistory history, HubCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(registry);
        var job = inputs.Job;
        var (record, manifest) = await VersionRecordFactory.CreateAsync(inputs, registry, history, cancellationToken).ConfigureAwait(false);
        var problems = DependencyChecks.Check(manifest, registry.FindRegistration(job.Plugin)!, registry, catalog);
        if (problems.Count > 0)
        {
            await RejectAsync(job.TrackingIssue, "the checks against the hub failed.", problems, cancellationToken).ConfigureAwait(false);
            return new ProposalResult(null, null, $"Rejected {job.Plugin} {job.Version}: {string.Join(" ", problems)}");
        }

        var changes = new ChangeSet(
            TrackingIssues.Branch(job.Plugin, record.Version),
            TrackingIssues.PullRequestTitle(job.Plugin, record.Version),
            string.Create(CultureInfo.InvariantCulture, $"{summary.TrimEnd()}\n\nTracking issue: #{job.TrackingIssue}\n"),
            new Dictionary<string, byte[]?>(StringComparer.Ordinal) { [RegistryPaths.Version(job.Plugin, record.Version)] = RegistryJson.Serialize(record) },
            [HubLabels.Version],
            AutoMerge: true);
        var number = await bot.OpenPullRequestAsync(HubLocations.Registry, changes, cancellationToken).ConfigureAwait(false);
        await bot.CommentAsync(HubLocations.Registry, job.TrackingIssue, Opened(record, number), cancellationToken).ConfigureAwait(false);
        return new ProposalResult(record, number, string.Create(CultureInfo.InvariantCulture, $"Opened #{number} for {job.Plugin} {record.Version} ({record.State.ToString().ToLowerInvariant()})."));
    }

    /// <summary>Rejects a version whose analysis failed, listing every error.</summary>
    public async Task<ProposalResult> RejectFailedAsync(BuildJob job, AnalysisReport analysis, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(analysis);
        var errors = analysis.Problems.Where(problem => problem.Severity == ProblemSeverity.Error)
            .Select(problem => problem.Location is { Length: > 0 } location ? $"{location}: {problem.Message}" : problem.Message);
        await RejectAsync(job.TrackingIssue, "the automated checks failed.", errors, cancellationToken).ConfigureAwait(false);
        return new ProposalResult(null, null, $"Rejected {job.Plugin} {job.Version}: the analysis failed.");
    }

    /// <summary>Comments the reason on a tracking issue and closes it.</summary>
    public async Task RejectAsync(int trackingIssue, string reason, IEnumerable<string> details, CancellationToken cancellationToken)
    {
        await bot.CommentAsync(HubLocations.Registry, trackingIssue, TrackingIssues.Rejection(reason, details), cancellationToken).ConfigureAwait(false);
        await bot.AddLabelsAsync(HubLocations.Registry, trackingIssue, [HubLabels.Rejected], cancellationToken).ConfigureAwait(false);
        await bot.CloseIssueAsync(HubLocations.Registry, trackingIssue, cancellationToken).ConfigureAwait(false);
    }

    private static string Opened(RegistryVersionRecord record, int pullRequest)
    {
        var next = record.State == VersionState.Review
            ? "It needs an administrator's review before it is published."
            : string.Create(CultureInfo.InvariantCulture, $"It is held until {record.HoldUntil:yyyy-MM-dd HH:mm} UTC and then published.");
        return string.Create(CultureInfo.InvariantCulture, $"The checks passed. Version pull request: #{pullRequest}. {next}\n");
    }
}
