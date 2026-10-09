using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Gates;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Json;
using RunesmithHub.Tool.Publishing;
using RunesmithHub.Tool.Tests.Support;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Tests.Gates;

public sealed class VersionGatesTests : IDisposable
{
    private const long RunId = 18_000_010;
    private const long Admin = 9001;
    private const long Owner = 9002;

    private readonly TestRegistry _registry = new();
    private readonly TemporaryFolder _folder = new();
    private readonly FakeGitHub _github = new();
    private readonly FakeArtifactSource _artifacts = new();
    private readonly FakeTimeProvider _clock = new(Samples.Time);

    public VersionGatesTests()
    {
        _github.Teams["admins"].Add(Admin);
        _github.Teams["owners"].Add(Owner);
        _github.Teams["admins"].Add(Owner);
        _registry.InitGit();
        _registry.Publisher(Samples.Publisher()).Registration(Samples.Registration());
        _registry.Commit("Register lumen.todo");
    }

    public void Dispose()
    {
        _folder.Dispose();
        _registry.Dispose();
    }

    private VersionGates Gates => new(_github, _artifacts, _registry.Git, _clock, FakeGitHub.Bot.Login);

    /// <summary>Runs the pipeline for a version: writes its artifacts and returns the record it proposes.</summary>
    private async Task<RegistryVersionRecord> BuildAsync(string version)
    {
        var package = Samples.WriteBuildOutput(_folder.Combine(version, "package"), version);
        var analysisFolder = _folder.Combine(version, "analysis");
        Directory.CreateDirectory(analysisFolder);
        var analysis = Samples.AnalysisFor(await _registry.LoadAsync(), package, version);
        await File.WriteAllBytesAsync(Path.Combine(analysisFolder, AnalysisReport.FileName), analysis, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(analysisFolder, BuildJob.FileName), ToolJson.Serialize(Samples.Job(version)), TestContext.Current.CancellationToken);
        _artifacts.Folders[(RunId, WorkflowArtifacts.Package)] = package;
        _artifacts.Folders[(RunId, WorkflowArtifacts.Analysis)] = analysisFolder;
        return await Samples.RecordAsync(await _registry.LoadAsync(), package, version, RunId, analysis);
    }

    /// <summary>Commits a change on a branch and registers a pull request for it.</summary>
    private async Task<GateInputs> PullRequestAsync(Action change, GitHubAccount? author = null, DateTimeOffset? createdAt = null)
    {
        _registry.Run("checkout", "-q", "-b", "change-" + _github.PullRequests.Count);
        change();
        var head = _registry.Commit("Change");
        _registry.Run("checkout", "-q", "main");
        var number = 40 + _github.PullRequests.Count;
        _github.PullRequests.Add(new PullRequest(number, "Change", "", "change", head, author ?? FakeGitHub.Bot, [HubLabels.Version], true, false, createdAt ?? Samples.Time, ""));
        return await VersionGates.LoadAsync(_github, _registry.Git, number, "main", TestContext.Current.CancellationToken);
    }

    private void Approve(GateInputs inputs, long by, string? commit = null, string state = "APPROVED") =>
        (_github.Reviews.TryGetValue(inputs.Pull.Number, out var list) ? list : _github.Reviews[inputs.Pull.Number] = [])
            .Add(new PullRequestReview(new GitHubAccount { Id = by, Login = "user" + by }, state, commit ?? inputs.Pull.HeadSha, Samples.Time, "Please fix the README."));

    [Fact]
    public async Task VerifyPassesARecordThatMatchesThePipeline()
    {
        var record = await BuildAsync("1.0.0");
        var inputs = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record));

        var result = await Gates.VerifyAsync(inputs, TestContext.Current.CancellationToken);

        Assert.Equal(CommitStatusState.Success, result.State);
        Assert.Equal("hub/verify", result.Context);
    }

    [Fact]
    public async Task VerifyFailsARecordEditedByHandAndNamesTheFields()
    {
        var record = await BuildAsync("1.0.0");
        var inputs = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record with { Summary = "Totally safe.", State = VersionState.Held }));

        var result = await Gates.VerifyAsync(inputs, TestContext.Current.CancellationToken);

        Assert.Equal(CommitStatusState.Failure, result.State);
        Assert.Contains("state", result.Lines.Single(), StringComparison.Ordinal);
        Assert.Contains("summary", result.Lines.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAcceptsAHeldVersionMovedToReview()
    {
        _registry.Version(Samples.PluginId, await BuildAsync("0.9.0") with { State = VersionState.Published });
        _registry.Commit("Publish 0.9.0");
        var record = await BuildAsync("1.0.0");
        Assert.Equal(VersionState.Held, record.State);
        var inputs = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record with { State = VersionState.Review, HoldUntil = null }));

        Assert.Equal(CommitStatusState.Success, (await Gates.VerifyAsync(inputs, TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task VerifyFailsWhenTheStoredAnalysisDoesNotMatchTheDigest()
    {
        var record = await BuildAsync("1.0.0");
        await File.WriteAllBytesAsync(Path.Combine(_artifacts.Folders[(RunId, WorkflowArtifacts.Analysis)], AnalysisReport.FileName),
            Samples.Analysis(_artifacts.Folders[(RunId, WorkflowArtifacts.Package)], "1.0.0", AnalysisOutcome.Review), TestContext.Current.CancellationToken);
        var inputs = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record));

        var result = await Gates.VerifyAsync(inputs, TestContext.Current.CancellationToken);

        Assert.Equal(CommitStatusState.Failure, result.State);
        Assert.Contains("analysisDigest", result.Lines.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAllowsOnlyStateChangesToExistingRecords()
    {
        var record = await BuildAsync("1.0.0");
        _registry.Version(Samples.PluginId, record);
        _registry.Commit("Add 1.0.0");

        var yank = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record with { State = VersionState.Yanked, YankReason = "Corrupts files." }));
        var edit = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record with { ReleaseNotes = "Rewritten." }));
        var delete = await PullRequestAsync(() => File.Delete(Path.Combine(_registry.Path, "plugins/lumen.todo/versions/1.0.0.json")));

        Assert.Equal(CommitStatusState.Success, (await Gates.VerifyAsync(yank, TestContext.Current.CancellationToken)).State);
        Assert.Equal(CommitStatusState.Failure, (await Gates.VerifyAsync(edit, TestContext.Current.CancellationToken)).State);
        Assert.Contains("never deleted", (await Gates.VerifyAsync(delete, TestContext.Current.CancellationToken)).Lines.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewWaitsForAnAdministratorsApprovalOfTheCurrentCommit()
    {
        var record = await BuildAsync("1.0.0");
        var inputs = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record));

        Assert.Equal(CommitStatusState.Pending, (await Gates.ReviewAsync(inputs, TestContext.Current.CancellationToken)).State);
        Approve(inputs, Admin, commit: new string('a', 40));
        Approve(inputs, 31337);
        Assert.Equal(CommitStatusState.Pending, (await Gates.ReviewAsync(inputs, TestContext.Current.CancellationToken)).State);
        Approve(inputs, Admin);
        Assert.Equal(CommitStatusState.Success, (await Gates.ReviewAsync(inputs, TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task ReviewCountsAnApprovalTheBotRecordedForApprove()
    {
        var record = await BuildAsync("1.0.0");
        var inputs = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record));
        await _github.CommentAsync(HubLocations.Registry, inputs.Pull.Number, Approvals.Marker(new GitHubAccount { Id = Admin, Login = "admin" }, inputs.Pull.HeadSha), TestContext.Current.CancellationToken);

        Assert.Equal(CommitStatusState.Success, (await Gates.ReviewAsync(inputs, TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task ReviewFailsAndCarriesTheRejectionWhenAnAdministratorRequestsChanges()
    {
        var record = await BuildAsync("1.0.0");
        var inputs = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record));
        Approve(inputs, Admin, state: "CHANGES_REQUESTED");

        var result = await Gates.ReviewAsync(inputs, TestContext.Current.CancellationToken);

        Assert.Equal(CommitStatusState.Failure, result.State);
        Assert.Equal("Please fix the README.", result.Rejection!.Body);
    }

    [Fact]
    public async Task ReviewLetsTheBotRestrictButNotLoosenOnItsOwn()
    {
        var record = await BuildAsync("1.0.0");
        _registry.Version(Samples.PluginId, record with { State = VersionState.Published });
        _registry.Commit("Add 1.0.0");

        var yankByBot = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record with { State = VersionState.Yanked }));
        var yankBySomeone = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record with { State = VersionState.Yanked }), new GitHubAccount { Id = 5, Login = "someone" });
        _registry.Version(Samples.PluginId, record with { State = VersionState.Yanked });
        _registry.Commit("Yank 1.0.0");
        var unyank = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record with { State = VersionState.Published }));

        Assert.Equal(CommitStatusState.Success, (await Gates.ReviewAsync(yankByBot, TestContext.Current.CancellationToken)).State);
        Assert.Equal(CommitStatusState.Pending, (await Gates.ReviewAsync(yankBySomeone, TestContext.Current.CancellationToken)).State);
        Assert.Equal(CommitStatusState.Pending, (await Gates.ReviewAsync(unyank, TestContext.Current.CancellationToken)).State);
        Approve(unyank, Admin);
        Assert.Equal(CommitStatusState.Success, (await Gates.ReviewAsync(unyank, TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task ReviewHoldsLiftingABlockForAnOwnerAndTheCoolingPeriodWithOneAdministrator()
    {
        _github.Teams["admins"].Remove(Admin);
        var entry = Block();
        _registry.Moderation(entry);
        _registry.Commit("Block");
        var inputs = await PullRequestAsync(() => _registry.Moderation(entry with { LiftedAt = Samples.Time, LiftedBy = Owner }));

        var waiting = await Gates.ReviewAsync(inputs, TestContext.Current.CancellationToken);
        Assert.Equal(CommitStatusState.Pending, waiting.State);
        Assert.Contains(waiting.Lines, line => line.Contains("owner's approval", StringComparison.Ordinal));
        Assert.Contains(waiting.Lines, line => line.Contains("cooling period", StringComparison.Ordinal));

        Approve(inputs, Owner);
        Assert.Equal(CommitStatusState.Pending, (await Gates.ReviewAsync(inputs, TestContext.Current.CancellationToken)).State);
        _clock.Advance(TimeSpan.FromHours(24));
        Assert.Equal(CommitStatusState.Success, (await Gates.ReviewAsync(inputs, TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task ReviewNeedsAnotherOwnerThanTheRequesterWithTwoAdministrators()
    {
        _github.Teams["owners"].Add(Admin);
        var entry = Block();
        _registry.Moderation(entry);
        _registry.Commit("Block");
        var inputs = await PullRequestAsync(() => _registry.Moderation(entry with { LiftedAt = Samples.Time, LiftedBy = Owner }));

        Approve(inputs, Owner);
        Assert.Equal(CommitStatusState.Pending, (await Gates.ReviewAsync(inputs, TestContext.Current.CancellationToken)).State);
        Approve(inputs, Admin);
        Assert.Equal(CommitStatusState.Success, (await Gates.ReviewAsync(inputs, TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task HoldWaitsUntilTheHoldEnds()
    {
        _registry.Version(Samples.PluginId, await BuildAsync("0.9.0") with { State = VersionState.Published });
        _registry.Commit("Publish 0.9.0");
        var record = await BuildAsync("1.0.0");
        var inputs = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record));

        Assert.Equal(CommitStatusState.Pending, Gates.Hold(inputs).State);
        _clock.Advance(TimeSpan.FromHours(24));
        Assert.Equal(CommitStatusState.Success, Gates.Hold(inputs).State);
    }

    [Fact]
    public async Task GateCommandSetsTheCommitStatus()
    {
        var record = await BuildAsync("1.0.0");
        var inputs = await PullRequestAsync(() => _registry.Version(Samples.PluginId, record));
        var tool = new ToolRunner();
        tool.GitHub.PullRequests.AddRange(_github.PullRequests);
        tool.Artifacts.Folders[(RunId, WorkflowArtifacts.Analysis)] = _artifacts.Folders[(RunId, WorkflowArtifacts.Analysis)];
        tool.Artifacts.Folders[(RunId, WorkflowArtifacts.Package)] = _artifacts.Folders[(RunId, WorkflowArtifacts.Package)];

        var (exitCode, output, error) = await tool.RunAsync("gate", "verify", "--pr", inputs.Pull.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--registry", _registry.Path, "--default-branch", "main");

        Assert.True(exitCode == 0, error);
        Assert.Contains("hub/verify", output, StringComparison.Ordinal);
        Assert.Equal((inputs.Pull.HeadSha, "hub/verify", CommitStatusState.Success), (tool.GitHub.Statuses.Single().Sha, tool.GitHub.Statuses.Single().Context, tool.GitHub.Statuses.Single().State));
    }

    private static RegistryModerationEntry Block() => new()
    {
        Id = 1,
        Target = new ModerationTarget { Kind = ModerationTargetKind.Versions, Plugin = Samples.PluginId, Range = VersionRange.Parse("1.0.0") },
        Action = ModerationAction.Block,
        Reason = ModerationReason.Broken,
        Note = "Broke startup.",
        CreatedAt = Samples.Time,
        CreatedBy = Admin,
    };
}
