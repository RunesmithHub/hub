using Microsoft.Extensions.Time.Testing;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Gates;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Publishing;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Publishing;

public sealed class ArtifactPublisherTests : IDisposable
{
    private const long RunId = 18_000_020;
    private readonly TestRegistry _registry = new();
    private readonly TemporaryFolder _folder = new();
    private readonly FakeGitHub _github = new();
    private readonly FakeArtifactSource _artifacts = new();
    private readonly FakeFeed _feed = new();

    public ArtifactPublisherTests()
    {
        _registry.Publisher(Samples.Publisher()).Registration(Samples.Registration());
        _github.Teams["admins"].Add(9001);
    }

    public void Dispose()
    {
        _folder.Dispose();
        _registry.Dispose();
    }

    private async Task<RegistryVersionRecord> MergeAsync(string version, bool banner = false)
    {
        var output = Samples.WriteBuildOutput(_folder.Combine("build", version), version, banner: banner);
        _artifacts.Folders[(RunId, WorkflowArtifacts.Package(Samples.PluginId, SemanticVersion.Parse(version)))] = output;
        var record = await Samples.RecordAsync(await _registry.LoadAsync(), output, version, RunId);
        _registry.Version(Samples.PluginId, record);
        return record;
    }

    private async Task<IReadOnlyList<Publication>> PublishAsync() =>
        (await new ArtifactPublisher(_github, _github, _artifacts, _feed, new FakeTimeProvider(Samples.Time), FakeGitHub.Bot.Login)
            .PublishAsync(await _registry.LoadAsync(), IndexState.Empty, _folder.Combine("work", Guid.NewGuid().ToString("N")), TestContext.Current.CancellationToken)).Publications;

    private void ApprovedPullRequest(string version)
    {
        var head = new string('c', 40);
        _github.PullRequests.Add(new PullRequest(60, $"Publish lumen.todo {version}", "", $"version/lumen.todo/{version}", head, FakeGitHub.Bot, [HubLabels.Version], false, true, Samples.Time, ""));
        _github.Reviews[60] = [new PullRequestReview(new GitHubAccount { Id = 9001, Login = "mod" }, "APPROVED", head, Samples.Time.AddHours(-1))];
    }

    [Fact]
    public async Task ReleasesTheFilesPushesTheContractsAndRecordsTheReviewer()
    {
        await MergeAsync("1.0.0", banner: true);
        ApprovedPullRequest("1.0.0");

        var publication = Assert.Single(await PublishAsync());

        Assert.Equal(("lumen.todo", "1.0.0", Samples.Time), (publication.Plugin, publication.Version, publication.PublishedAt));
        Assert.Equal((ReviewState.Reviewed, (long?)9001), (publication.Review.State, publication.Review.By));
        var release = _github.PackageReleases["lumen.todo@1.0.0"];
        Assert.Equal(
            ["banner-1280.webp", "banner-640.webp", "icon-128.png", "icon-256.png", "icon-64.png", "lumen.todo-1.0.0.cdx.json", "lumen.todo-1.0.0.rsplugin", "lumen.todo-1.0.0.source.tar.gz"],
            release.Assets.Select(asset => asset.Name).Order(StringComparer.Ordinal));
        Assert.True(_feed.Packages.ContainsKey("RunesmithHub.Lumen.Todo.Contracts@1.0.0"));
    }

    [Fact]
    public async Task SkipsWhatAlreadyExistsSoARunCanBeRepeated()
    {
        await MergeAsync("1.0.0");
        ApprovedPullRequest("1.0.0");
        await PublishAsync();
        var uploads = _github.Uploads.Count;

        await PublishAsync();

        Assert.Equal(uploads, _github.Uploads.Count);
    }

    [Fact]
    public async Task StopsWhenTheBuildOutputDoesNotMatchTheRecord()
    {
        var record = await MergeAsync("1.0.0");
        await File.WriteAllTextAsync(Path.Combine(_artifacts.Folders[(RunId, WorkflowArtifacts.Package(Samples.PluginId, SemanticVersion.Parse("1.0.0")))], "lumen.todo-1.0.0.source.tar.gz"), "tampered", TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<ToolException>(PublishAsync);

        Assert.Contains("source archive", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_github.Uploads);
        Assert.Equal(VersionState.Review, record.State);
    }

    [Fact]
    public async Task RefusesAReviewedVersionWithoutAnAdministratorsApproval()
    {
        await MergeAsync("1.0.0");
        _github.PullRequests.Add(new PullRequest(60, "Publish", "", "version/lumen.todo/1.0.0", new string('c', 40), FakeGitHub.Bot, [], false, true, Samples.Time, ""));

        var exception = await Assert.ThrowsAsync<ToolException>(PublishAsync);

        Assert.Contains("no administrator approved", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkipsVersionsTheDeployedIndexPublishedAndBlockedPlugins()
    {
        await MergeAsync("1.0.0");
        _registry.Registration(Samples.Registration(state: RegistrationState.Blocked));

        Assert.Empty(await PublishAsync());
    }

    [Fact]
    public async Task AnnouncesAndClosesTrackingIssues()
    {
        _github.Issues.Add(new Issue(501, "lumen.todo 1.0.0", "", true, [HubLabels.Tracking], FakeGitHub.Bot, Samples.Time, null, ""));
        var publication = new Publication
        {
            Plugin = "lumen.todo",
            Version = "1.0.0",
            PublishedAt = Samples.Time,
            Review = new VersionReview { State = ReviewState.Automated },
            TrackingIssue = 501,
            Release = "https://github.com/RunesmithHub/packages/releases/tag/lumen.todo%401.0.0",
        };

        await new PublishAnnouncer(_github).AnnounceAsync([publication], TestContext.Current.CancellationToken);

        Assert.Contains("https://hub.runesmith.dev/plugins/lumen.todo", _github.CommentsOn(501).Single().Body, StringComparison.Ordinal);
        Assert.False(_github.IssueNumber(501).Open);
    }

    [Fact]
    public void ApprovalsUseTheLatestReviewOfEachPersonAtTheHeadCommit()
    {
        var head = new string('d', 40);
        var ada = new GitHubAccount { Id = 1, Login = "ada" };
        var reviews = new[]
        {
            new PullRequestReview(ada, "APPROVED", head, Samples.Time),
            new PullRequestReview(ada, "CHANGES_REQUESTED", head, Samples.Time.AddMinutes(5)),
            new PullRequestReview(new GitHubAccount { Id = 2, Login = "bo" }, "COMMENTED", head, Samples.Time),
        };

        Assert.Empty(Approvals.Of(reviews, [], head, null));
        Assert.True(Approvals.ChangesRequested(reviews, new HashSet<long> { 1 }));
    }
}
