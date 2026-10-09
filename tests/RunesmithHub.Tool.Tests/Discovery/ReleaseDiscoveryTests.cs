using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Discovery;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Discovery;

public sealed class ReleaseDiscoveryTests : IDisposable
{
    private readonly TestRegistry _registry = new();
    private readonly FakeGitHub _github = new();
    private readonly GitHubUser _owner;

    public ReleaseDiscoveryTests()
    {
        _owner = _github.AddUser(Samples.OwnerId, "lumen-labs", OwnerType.Organization);
        _github.AddRepository(Samples.RepositoryId, Samples.RepositoryName, _owner);
        _registry.Publisher(Samples.Publisher()).Registration(Samples.Registration());
    }

    public void Dispose() => _registry.Dispose();

    private void Release(string tag, bool draft = false, bool prerelease = false, string? commit = "0123456789abcdef0123456789abcdef01234567")
    {
        if (!_github.Releases.TryGetValue(Samples.RepositoryName, out var list))
            _github.Releases[Samples.RepositoryName] = list = [];
        list.Insert(0, new GitHubRelease(list.Count + 1, tag, draft, prerelease, $"Notes for {tag}", Samples.Maintainer));
        if (commit is not null)
            _github.Tags[$"{Samples.RepositoryName}:{tag}"] = commit;
    }

    private async Task<DiscoveryResult> DiscoverAsync(string? plugin = null) =>
        await new ReleaseDiscovery(_github, _github).DiscoverAsync(await _registry.LoadAsync(), plugin, TestContext.Current.CancellationToken);

    [Fact]
    public async Task EmitsABuildJobAndOpensATrackingIssueForANewRelease()
    {
        Release("v1.4.0");

        var result = await DiscoverAsync();

        var job = Assert.Single(result.Jobs);
        Assert.Equal(("lumen.todo", "1.4.0", "v1.4.0"), (job.Plugin, job.Version, job.Tag));
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", job.Commit);
        Assert.Equal(Samples.Maintainer.Id, job.ReleaseAuthor.Id);
        Assert.Equal("Notes for v1.4.0", job.ReleaseNotes);
        var issue = _github.IssueNumber(job.TrackingIssue);
        Assert.Equal("lumen.todo 1.4.0", issue.Title);
        Assert.Contains("@ada", issue.Body, StringComparison.Ordinal);
        Assert.Equal([HubLabels.Tracking], issue.Labels);
    }

    [Fact]
    public async Task SkipsDraftsForeignTagsMismatchedPreReleasesAndKnownVersions()
    {
        Release("v1.0.0");
        Release("v1.1.0", draft: true);
        Release("release-1.2.0");
        Release("v1.3.0-beta.1", prerelease: false);
        Release("v1.3.0", prerelease: true);
        Release("v1.3.1-rc.1", prerelease: true);
        Release("vnext");
        var registry = await _registry.LoadAsync();
        _github.Issues.Add(new Issue(7, "lumen.todo 1.3.1-rc.1", "", false, [HubLabels.Tracking], Samples.Maintainer, Samples.Time, Samples.Time, ""));

        var result = await new ReleaseDiscovery(_github, _github).DiscoverAsync(registry with
        {
            Versions = new Dictionary<string, IReadOnlyList<RegistryVersionRecord>> { ["lumen.todo"] = [await KnownRecordAsync()] },
        }, null, TestContext.Current.CancellationToken);

        Assert.Empty(result.Jobs);
        Assert.Contains(result.Notes, note => note.Contains("vnext is not a valid version tag", StringComparison.Ordinal));
        Assert.Contains(result.Notes, note => note.Contains("v1.3.0 is marked as a pre-release", StringComparison.Ordinal));
        Assert.Contains(result.Notes, note => note.Contains("v1.3.0-beta.1 is not marked as a pre-release", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IgnoresRepositoriesThatChangedOwner()
    {
        var stranger = _github.AddUser(4242, "someone-else", OwnerType.User);
        _github.AddRepository(Samples.RepositoryId, "someone-else/lumen-todo", stranger);
        Release("v1.4.0");

        var result = await DiscoverAsync();

        Assert.Empty(result.Jobs);
        Assert.Contains(result.Notes, note => note.Contains("nightly audit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LooksOnlyAtTheRequestedPluginAndRejectsUnknownOnes()
    {
        Release("v2.0.0");

        Assert.Single((await DiscoverAsync("lumen.todo")).Jobs);
        await Assert.ThrowsAsync<ToolException>(() => DiscoverAsync("lumen.nothing"));
    }

    [Fact]
    public async Task SkipsFrozenPluginsAndSuspendedPublishers()
    {
        Release("v1.4.0");
        _registry.Publisher(Samples.Publisher() with { State = PublisherState.Suspended });

        Assert.Empty((await DiscoverAsync()).Jobs);
    }

    private async Task<RegistryVersionRecord> KnownRecordAsync()
    {
        using var folder = new TemporaryFolder();
        return await Samples.RecordAsync(await _registry.LoadAsync() with { Versions = new Dictionary<string, IReadOnlyList<RegistryVersionRecord>>() },
            Samples.WriteBuildOutput(folder.Combine("out"), "1.0.0"), "1.0.0");
    }
}
