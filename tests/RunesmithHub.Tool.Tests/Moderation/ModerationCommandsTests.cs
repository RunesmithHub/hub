using Microsoft.Extensions.Time.Testing;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Moderation;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Moderation;

public sealed class ModerationCommandsTests : IDisposable
{
    private const long Admin = 9001;
    private const int Issue = 300;

    private readonly TestRegistry _registry = new();
    private readonly TemporaryFolder _folder = new();
    private readonly FakeGitHub _github = new();

    public ModerationCommandsTests()
    {
        _github.Teams["admins"].Add(Admin);
        _github.AddUser(Admin, "mod");
        _registry.Publisher(Samples.Publisher()).Registration(Samples.Registration());
    }

    public void Dispose()
    {
        _folder.Dispose();
        _registry.Dispose();
    }

    private async Task<CommandOutcome?> RunAsync(string comment, long actor = Admin, int issue = Issue) =>
        await new ModerationCommands(_github, _github, new FakeTimeProvider(Samples.Time)).RunAsync(comment, actor, issue, await _registry.LoadAsync(), TestContext.Current.CancellationToken);

    private async Task AddVersionsAsync(params string[] versions)
    {
        foreach (var version in versions)
            _registry.Version(Samples.PluginId, await Samples.RecordAsync(await _registry.LoadAsync(), Samples.WriteBuildOutput(_folder.Combine(version), version), version) with { State = VersionState.Published });
    }

    private T Written<T>(string path)
        where T : class => RegistryJson.Deserialize<T>(_github.OpenedPullRequests.Single().Changes.Files[path]!);

    [Fact]
    public async Task OnlyAdministratorsMayUseCommands()
    {
        var outcome = await RunAsync("/freeze lumen.todo broken", actor: 5);

        Assert.Equal("Only administrators can use hub commands.", outcome!.Reply);
        Assert.Empty(_github.OpenedPullRequests);
        Assert.Equal(outcome.Reply, _github.CommentsOn(Issue).Single().Body);
    }

    [Fact]
    public async Task FreezingAPluginAddsAnEntryAndFreezesTheRegistration()
    {
        var outcome = await RunAsync("/freeze lumen.todo compromised account");

        var (number, changes) = _github.OpenedPullRequests.Single();
        Assert.Equal(number, outcome!.PullRequest);
        Assert.Equal("moderation/00001", changes.Branch);
        Assert.Equal([HubLabels.Moderation], changes.Labels);
        var entry = Written<RegistryModerationEntry>("moderation/00001.json");
        Assert.Equal((ModerationAction.Freeze, ModerationReason.CompromisedAccount, Admin), (entry.Action, entry.Reason, entry.CreatedBy));
        Assert.Equal("Installs are paused while Runesmith looks into a report.", entry.Note);
        Assert.Equal(RegistrationState.Frozen, Written<Protocol.Registry.Registration>("plugins/lumen.todo/registration.json").State);
        Assert.Contains("/freeze lumen.todo compromised account", changes.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlockingARangeBlocksTheVersionsInIt()
    {
        await AddVersionsAsync("1.3.0", "1.4.0", "1.4.2");

        await RunAsync("/block lumen.todo@>=1.3.0 <1.4.2 malicious Stole tokens.");

        var files = _github.OpenedPullRequests.Single().Changes.Files;
        Assert.Equal(["moderation/00001.json", "plugins/lumen.todo/versions/1.3.0.json", "plugins/lumen.todo/versions/1.4.0.json"], files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(VersionState.Blocked, Written<RegistryVersionRecord>("plugins/lumen.todo/versions/1.4.0.json").State);
        Assert.Equal("Stole tokens.", Written<RegistryModerationEntry>("moderation/00001.json").Note);
    }

    [Fact]
    public async Task UnblockingFollowsTheStrictPath()
    {
        await AddVersionsAsync("1.4.0");
        _registry.Version(Samples.PluginId, (await _registry.LoadAsync()).VersionsOf(Samples.PluginId)[0] with { State = VersionState.Blocked });
        _registry.Moderation(new RegistryModerationEntry
        {
            Id = 4,
            Target = new ModerationTarget { Kind = ModerationTargetKind.Versions, Plugin = Samples.PluginId, Range = VersionRange.Parse("1.4.0") },
            Action = ModerationAction.Block,
            Reason = ModerationReason.Broken,
            Note = "Broke startup.",
            CreatedAt = Samples.Time,
            CreatedBy = Admin,
        });

        var outcome = await RunAsync("/unblock lumen.todo@1.4.0");

        var changes = _github.OpenedPullRequests.Single().Changes;
        Assert.Equal([HubLabels.Moderation, HubLabels.StrictPath], changes.Labels);
        Assert.Contains("waits 24 hours", outcome!.Reply, StringComparison.Ordinal);
        var lifted = Written<RegistryModerationEntry>("moderation/00004.json");
        Assert.Equal((Samples.Time, (long?)Admin), (lifted.LiftedAt!.Value, lifted.LiftedBy));
        Assert.Equal(VersionState.Published, Written<RegistryVersionRecord>("plugins/lumen.todo/versions/1.4.0.json").State);
    }

    [Fact]
    public async Task ExplainsWhenThereIsNothingToLift()
    {
        var outcome = await RunAsync("/unfreeze lumen.todo");

        Assert.Contains("no active freeze", outcome!.Reply, StringComparison.Ordinal);
        Assert.Empty(_github.OpenedPullRequests);
    }

    [Fact]
    public async Task VerifyingRaisesTheTierAndVerifiesThePublisher()
    {
        await RunAsync("/verify lumen.todo");

        var changes = _github.OpenedPullRequests.Single().Changes;
        Assert.Contains(HubLabels.StrictPath, changes.Labels);
        Assert.Equal(PluginTier.Verified, Written<Protocol.Registry.Registration>("plugins/lumen.todo/registration.json").Tier);
        Assert.Equal(Admin, Written<PublisherFile>("publishers/lumen.json").Verified!.By);
    }

    [Fact]
    public async Task SuspendingAndDeleting()
    {
        await RunAsync("/suspend publisher:lumen Unresponsive during an incident.");
        Assert.Equal(PublisherState.Suspended, Written<PublisherFile>("publishers/lumen.json").State);

        _github.OpenedPullRequests.Clear();
        await RunAsync("/delete lumen.todo");
        Assert.Equal(RegistrationState.Deleted, Written<Protocol.Registry.Registration>("plugins/lumen.todo/registration.json").State);
    }

    [Fact]
    public async Task ApprovingRecordsTheApprovalOnTheVersionPullRequest()
    {
        TrackingIssueWithPullRequest(out var pull);

        var outcome = await RunAsync("/approve");

        Assert.Equal(pull.Number, outcome!.PullRequest);
        var marker = _github.CommentsOn(pull.Number).Single().Body;
        var approval = Tool.Gates.Approvals.Of([], [new IssueComment(1, FakeGitHub.Bot, marker, Samples.Time)], pull.HeadSha, FakeGitHub.Bot.Login).Single();
        Assert.Equal(Admin, approval.By.Id);
    }

    [Fact]
    public async Task RejectingClosesThePullRequestAndTheTrackingIssue()
    {
        TrackingIssueWithPullRequest(out var pull);

        await RunAsync("/reject The icon copies another plugin's.");

        Assert.Equal([pull.Number], _github.ClosedPullRequests);
        Assert.False(_github.IssueNumber(Issue).Open);
        Assert.Equal("The icon copies another plugin's.", Tool.Versions.TrackingIssues.RejectionReason(_github.CommentsOn(Issue)[0].Body));
    }

    [Fact]
    public async Task MovingAHeldVersionToReviewUpdatesItsBranch()
    {
        TrackingIssueWithPullRequest(out var pull);
        var record = await Samples.RecordAsync(await _registry.LoadAsync(), Samples.WriteBuildOutput(_folder.Combine("held"), "1.0.0"), "1.0.0") with
        {
            State = VersionState.Held,
            HoldUntil = Samples.Time.AddHours(20),
        };
        _github.Files["RunesmithHub/registry:plugins/lumen.todo/versions/1.0.0.json"] = RegistryJson.Serialize(record);

        await RunAsync("/review");

        var update = _github.BranchUpdates.Single();
        Assert.Equal(pull.HeadRef, update.Branch);
        var moved = RegistryJson.Deserialize<RegistryVersionRecord>(update.Files["plugins/lumen.todo/versions/1.0.0.json"]!);
        Assert.Equal((VersionState.Review, (DateTimeOffset?)null), (moved.State, moved.HoldUntil));
    }

    [Fact]
    public async Task VersionCommandsWorkOnlyOnTrackingIssues()
    {
        var outcome = await RunAsync("/approve");

        Assert.Contains("tracking issue", outcome!.Reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftsAnAdvisory()
    {
        var outcome = await RunAsync("/advisory lumen.todo@1.4.0");

        Assert.Contains("security/advisories", outcome!.Reply, StringComparison.Ordinal);
        Assert.Single(_github.Advisories);
    }

    [Fact]
    public async Task RepliesWithUsageForMalformedCommands()
    {
        var outcome = await RunAsync("/freeze lumen.todo");

        Assert.Contains("needs a reason", outcome!.Reply, StringComparison.Ordinal);
        Assert.Contains("Commands:", outcome.Reply, StringComparison.Ordinal);
    }

    private void TrackingIssueWithPullRequest(out PullRequest pull)
    {
        _github.Issues.Add(new Issue(Issue, "lumen.todo 1.0.0", "", true, [HubLabels.Tracking], FakeGitHub.Bot, Samples.Time, null, ""));
        pull = new PullRequest(41, "Publish lumen.todo 1.0.0", "", "version/lumen.todo/1.0.0", new string('b', 40), FakeGitHub.Bot, [HubLabels.Version], true, false, Samples.Time, "");
        _github.PullRequests.Add(pull);
    }
}
