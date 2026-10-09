using System.Text;
using Microsoft.Extensions.Time.Testing;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Audit;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Audit;

public sealed class NightlyAuditTests : IDisposable
{
    private readonly TestRegistry _registry = new();
    private readonly TemporaryFolder _folder = new();
    private readonly FakeGitHub _github = new();
    private readonly FakeDownloader _downloads = new();
    private readonly FakeAdvisories _advisories = new();
    private readonly FakeTimeProvider _clock = new(Samples.Time);
    private readonly TestKeys _keys = new(Samples.Time);
    private readonly GitHubUser _owner;

    public NightlyAuditTests()
    {
        _registry.Publisher(Samples.Publisher()).Registration(Samples.Registration());
        _owner = _github.AddUser(Samples.OwnerId, "lumen-labs", OwnerType.Organization);
        _github.AddRepository(Samples.RepositoryId, Samples.RepositoryName, _owner);
    }

    public void Dispose()
    {
        _keys.Dispose();
        _folder.Dispose();
        _registry.Dispose();
    }

    private async Task<IReadOnlyList<AuditFinding>> RunAsync(IndexState? deployed = null) =>
        await new NightlyAudit(_github, _github, _downloads, _advisories, _clock)
            .RunAsync(await _registry.LoadAsync(), TrustRoot.Verify([_keys.Root]), deployed, TestContext.Current.CancellationToken);

    [Fact]
    public async Task FindsNothingWhenAllIsWell() => Assert.Empty(await RunAsync());

    [Fact]
    public async Task FreezesAPluginWhoseRepositoryMovedToAnotherOwner()
    {
        _github.AddRepository(Samples.RepositoryId, "thief/lumen-todo", _github.AddUser(666, "thief"));

        var finding = Assert.Single(await RunAsync());

        Assert.Equal("lumen.todo: Repository", finding.Title);
        Assert.Contains("thief", _github.Issues.Single().Body, StringComparison.Ordinal);
        var registration = RegistryJson.Deserialize<Protocol.Registry.Registration>(_github.OpenedPullRequests.Single().Changes.Files["plugins/lumen.todo/registration.json"]!);
        Assert.Equal(RegistrationState.Frozen, registration.State);
    }

    [Fact]
    public async Task DoesNotRepeatOpenIssuesOrPendingFreezes()
    {
        _github.Repositories.Clear();
        await RunAsync();
        _github.PullRequests.Add(new PullRequest(70, _github.OpenedPullRequests[0].Changes.Title, "", "audit/freeze", new string('e', 40), FakeGitHub.Bot, [], true, false, Samples.Time, ""));

        await RunAsync();

        Assert.Single(_github.Issues);
        Assert.Single(_github.OpenedPullRequests);
    }

    [Fact]
    public async Task FlagsVerifiedPluginsThatNoLongerMeetTheRequirements()
    {
        _registry.Registration(Samples.Registration(tier: PluginTier.Verified));
        _github.BranchRules = new BranchRules(BlocksDeletion: true, BlocksForcePushes: false);

        var finding = Assert.Single(await RunAsync());

        Assert.Equal("lumen.todo: Verified requirement", finding.Title);
        Assert.Contains("- [ ] The default branch blocks force pushes (not met)", finding.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAdvisoriesForPackagesInPublishedVersions()
    {
        var record = await Samples.RecordAsync(await _registry.LoadAsync(), Samples.WriteBuildOutput(_folder.Combine("out"), "1.0.0"), "1.0.0");
        _registry.Version(Samples.PluginId, record);
        _downloads.Files[record.Sbom!.Urls[0]] = await File.ReadAllBytesAsync(_folder.Combine("out", "lumen.todo-1.0.0.cdx.json"), TestContext.Current.CancellationToken);
        _advisories.Known[new PackageVersion("Humanizer.Core", "2.14.1")] = [new Advisory("GHSA-xxxx-yyyy-zzzz", "Regular expression denial of service.", "CRITICAL")];
        var deployed = IndexWith(record);

        var finding = Assert.Single(await RunAsync(deployed));

        Assert.Equal("lumen.todo 1.0.0: Vulnerability GHSA-xxxx-yyyy-zzzz", finding.Title);
        Assert.Contains("@ada", finding.Body, StringComparison.Ordinal);
        Assert.Contains("/freeze lumen.todo@1.0.0 vulnerability", finding.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WarnsThirtyDaysBeforeSignedMetadataExpires()
    {
        _clock.Advance(TimeSpan.FromDays(340));

        var finding = Assert.Single(await RunAsync());

        Assert.StartsWith("Index: Root expires", finding.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsNuGetPackagesFromCycloneDx()
    {
        var packages = NightlyAudit.NuGetComponents(Encoding.UTF8.GetBytes("""
            { "components": [ { "purl": "pkg:nuget/Newtonsoft.Json@13.0.3" }, { "purl": "pkg:npm/left-pad@1.0.0" }, { "name": "no purl" } ] }
            """));

        Assert.Equal([new PackageVersion("Newtonsoft.Json", "13.0.3")], packages);
    }

    private static IndexState IndexWith(RegistryVersionRecord record)
    {
        var plugin = new PluginRecord
        {
            Type = PayloadTypes.Plugin,
            Version = 1,
            Id = Samples.PluginId,
            Name = record.Name,
            Publisher = "lumen",
            Tier = PluginTier.Unverified,
            State = PluginState.Listed,
            Repository = new RecordRepository { Id = Samples.RepositoryId, Url = "https://github.com/" + Samples.RepositoryName, Path = "." },
            Maintainers = [Samples.Maintainer],
            Summary = record.Summary,
            Categories = record.Categories,
            Keywords = [],
            Icon = record.Assets!.Icon,
            Gallery = [],
            Versions = [record.ToIndexRecord() with { State = VersionState.Published, PublishedAt = Samples.Time }],
        };
        var bytes = IndexJson.Serialize(plugin);
        var meta = FileMeta.Of(1, bytes);
        var snapshot = new SnapshotPayload
        {
            Type = PayloadTypes.Snapshot,
            Version = 1,
            Expires = Samples.Time.AddDays(90),
            Plugins = new Dictionary<string, FileMeta> { [Samples.PluginId] = meta },
            Publishers = meta,
            Policy = meta,
        };
        return IndexState.Create(new Dictionary<string, byte[]> { [IndexPaths.Plugin(meta.Sha256)] = bytes }, null, new SignedFile<SnapshotPayload>("snapshot/x.json", snapshot), null);
    }
}
