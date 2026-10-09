using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Updating;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.IndexBuild;

/// <summary>
/// Runs the signing jobs' commands on a registry with a publisher, a registration and two versions: a bootstrap publish, a moderation run
/// that blocks one version, and a daily refresh, and checks each output the way Runesmith does.
/// </summary>
public sealed class IndexEndToEndTests : IDisposable
{
    private readonly TestRegistry _registry = new();
    private readonly TemporaryFolder _folder = new();
    private readonly ToolRunner _tool = new();
    private readonly TestKeys _keys;

    public IndexEndToEndTests() => _keys = new TestKeys(_tool.Clock.GetUtcNow());

    public void Dispose()
    {
        _keys.Dispose();
        _folder.Dispose();
        _registry.Dispose();
    }

    private async Task<string> SetUpRegistryAsync()
    {
        _registry.Root(1, _keys.Root).Publisher(Samples.Publisher()).Registration(Samples.Registration());
        var first = await Samples.RecordAsync(await _registry.LoadAsync(), Samples.WriteBuildOutput(_folder.Combine("build-1.0.0"), "1.0.0"), "1.0.0");
        _registry.Version(Samples.PluginId, first);
        var second = await Samples.RecordAsync(await _registry.LoadAsync(), Samples.WriteBuildOutput(_folder.Combine("build-1.1.0"), "1.1.0", banner: true), "1.1.0");
        _registry.Version(Samples.PluginId, second);
        Assert.Equal(VersionState.Review, first.State);
        Assert.Equal(VersionState.Held, second.State);

        var publications = _folder.Combine("publications.json");
        Publication.Write(publications,
        [
            Published("1.0.0", first.TrackingIssue, new VersionReview { State = ReviewState.Reviewed, By = 9001, At = Samples.Time.AddHours(-2) }),
            Published("1.1.0", second.TrackingIssue, second.Review),
        ]);
        return publications;
    }

    private static Publication Published(string version, int trackingIssue, VersionReview review) => new()
    {
        Plugin = Samples.PluginId,
        Version = version,
        PublishedAt = Samples.Time,
        Review = review,
        TrackingIssue = trackingIssue,
        Release = $"https://github.com/RunesmithHub/packages/releases/tag/lumen.todo%40{version}",
    };

    /// <summary>Runs an index build on a copy of an index directory, as a signing job does on a fresh checkout of the index branch.</summary>
    private async Task<string> RunIndexAsync(string operation, string current, string output, params string[] extra)
    {
        if (current != output)
            CopyDirectory(current, output);
        var (exitCode, stdout, error) = await _tool.RunAsync(["index", operation, "--registry", _registry.Path, "--index", output, .. extra]);
        Assert.True(exitCode == 0, error);
        return stdout;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private async Task<IndexView> VerifyAsync(string directory, ITrustStore store)
    {
        var result = await new IndexUpdater(new DirectoryIndexFetcher(directory), store, _keys.Root, _tool.Clock).UpdateAsync(TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Failure?.Message);
        return result.View;
    }

    [Fact]
    public async Task PublishesModeratesAndRefreshesAnIndexThatVerifies()
    {
        var publications = await SetUpRegistryAsync();
        var nothing = _folder.Combine("nothing-deployed");
        Directory.CreateDirectory(nothing);
        var client = new InMemoryTrustStore();

        _keys.Use(_tool.Environment, OnlineRole.Publish, OnlineRole.Moderation, OnlineRole.Timestamp);
        var published = _folder.Combine("published");
        var notes = await RunIndexAsync("publish", nothing, published, "--publications", publications);
        Assert.Contains("bootstrapping", notes, StringComparison.Ordinal);
        var first = await VerifyAsync(published, client);
        var record = first.Plugins[Samples.PluginId];
        Assert.Equal(["1.1.0", "1.0.0"], record.Versions.Select(version => version.Version.ToString()));
        Assert.All(record.Versions, version => Assert.Equal(VersionState.Published, version.State));
        Assert.All(record.Versions, version => Assert.NotNull(version.Package));
        Assert.All(record.Versions, version => Assert.Empty(version.Analysis.Triggers));
        Assert.Equal(9001, record.FindVersion(SemanticVersion.Parse("1.0.0"))!.Review.By);
        Assert.NotNull(record.Banner);
        Assert.Equal("Lumen Labs", first.Publishers.Find("lumen")!.DisplayName);
        Assert.Equal(24, first.Policy.HoldHours);
        Assert.Empty(first.Moderation.Entries);

        _registry.Moderation(new RegistryModerationEntry
        {
            Id = 1,
            Target = new ModerationTarget { Kind = ModerationTargetKind.Versions, Plugin = Samples.PluginId, Range = VersionRange.Parse("1.0.0") },
            Action = ModerationAction.Block,
            Reason = ModerationReason.Malicious,
            Note = "Sent tokens to a webhook.",
            CreatedAt = Samples.Time,
            CreatedBy = 9001,
        });
        _keys.Use(_tool.Environment, OnlineRole.Moderation, OnlineRole.Timestamp);
        _tool.Clock.Advance(TimeSpan.FromHours(1));
        var moderated = _folder.Combine("moderated");
        await RunIndexAsync("moderate", published, moderated);
        var second = await VerifyAsync(moderated, client);
        Assert.Equal(first.Snapshot.Version, second.Snapshot.Version);
        Assert.Equal(first.Moderation.Version + 1, second.Moderation.Version);
        Assert.True(File.Exists(Path.Combine(moderated, IndexPaths.Moderation(first.Timestamp.Moderation.Sha256))));
        var catalog = new HubCatalog(second);
        Assert.Equal(VersionState.Blocked, catalog.GetEffectiveState(Samples.PluginId, SemanticVersion.Parse("1.0.0"))!.State);
        Assert.Equal(VersionState.Published, catalog.GetEffectiveState(Samples.PluginId, SemanticVersion.Parse("1.1.0"))!.State);

        _keys.Use(_tool.Environment, OnlineRole.Timestamp);
        _tool.Clock.Advance(TimeSpan.FromDays(1));
        var refreshed = _folder.Combine("refreshed");
        await RunIndexAsync("refresh", moderated, refreshed);
        var third = await VerifyAsync(refreshed, client);
        Assert.Equal(second.Timestamp.Version + 1, third.Timestamp.Version);
        Assert.Equal(second.Snapshot.Version, third.Snapshot.Version);
        Assert.Equal(second.Moderation.Version, third.Moderation.Version);
        Assert.Equal(_tool.Clock.GetUtcNow().AddDays(3), third.Timestamp.Expires);

        var fresh = await VerifyAsync(refreshed, new InMemoryTrustStore());
        Assert.Equal(VersionState.Blocked, new HubCatalog(fresh).GetEffectiveState(Samples.PluginId, SemanticVersion.Parse("1.0.0"))!.State);
    }

    [Fact]
    public async Task SignsTheTimestampInALaterStepWhenThePublishJobHasNoTimestampKey()
    {
        var publications = await SetUpRegistryAsync();
        var nothing = _folder.Combine("nothing-deployed");
        Directory.CreateDirectory(nothing);
        var staged = _folder.Combine("staged");

        _keys.Use(_tool.Environment, OnlineRole.Publish);
        var notes = await RunIndexAsync("publish", nothing, staged, "--publications", publications);
        Assert.Contains("No new timestamp", notes, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(staged, IndexPaths.Timestamp)));

        _keys.Use(_tool.Environment, OnlineRole.Moderation);
        await RunIndexAsync("moderate", nothing, staged);
        _keys.Use(_tool.Environment, OnlineRole.Timestamp);
        await RunIndexAsync("timestamp", nothing, staged);

        var view = await VerifyAsync(staged, new InMemoryTrustStore());
        Assert.Equal(1, view.Snapshot.Version);
        Assert.Equal(1, view.Moderation.Version);
    }

    [Fact]
    public async Task KeepsTheSnapshotWhenNothingChangedAndRaisesARecordVersionWhenItDoes()
    {
        var publications = await SetUpRegistryAsync();
        var nothing = _folder.Combine("nothing-deployed");
        Directory.CreateDirectory(nothing);
        _keys.Use(_tool.Environment, OnlineRole.Publish, OnlineRole.Moderation, OnlineRole.Timestamp);
        var first = _folder.Combine("first");
        await RunIndexAsync("publish", nothing, first, "--publications", publications);
        var before = await VerifyAsync(first, new InMemoryTrustStore());

        var unchanged = _folder.Combine("unchanged");
        var notes = await RunIndexAsync("publish", first, unchanged);
        Assert.Contains("up to date", notes, StringComparison.Ordinal);
        var same = await VerifyAsync(unchanged, new InMemoryTrustStore());
        Assert.Equal(before.Snapshot.Version, same.Snapshot.Version);
        Assert.Equal(VersionState.Published, same.Plugins[Samples.PluginId].Versions[0].State);

        var registration = Samples.Registration() with { State = RegistrationState.Deprecated, Deprecated = new Deprecation { Reason = "Use something else." } };
        _registry.Registration(registration);
        var changed = _folder.Combine("changed");
        await RunIndexAsync("publish", unchanged, changed);
        var after = await VerifyAsync(changed, new InMemoryTrustStore());
        Assert.Equal(before.Snapshot.Version + 1, after.Snapshot.Version);
        Assert.Equal(before.Plugins[Samples.PluginId].Version + 1, after.Plugins[Samples.PluginId].Version);
        Assert.Equal(PluginState.Deprecated, after.Plugins[Samples.PluginId].State);
        Assert.Equal("1.1.0", after.Plugins[Samples.PluginId].Versions[0].Version.ToString());
        Assert.NotNull(after.Plugins[Samples.PluginId].Versions[0].PublishedAt);
    }

    [Fact]
    public async Task ShowsMergedVersionsAsHeldOrInReviewUntilAPublishRunReleasesThem()
    {
        await SetUpRegistryAsync();
        var nothing = _folder.Combine("nothing-deployed");
        Directory.CreateDirectory(nothing);
        _keys.Use(_tool.Environment, OnlineRole.Publish, OnlineRole.Moderation, OnlineRole.Timestamp);
        var output = _folder.Combine("out");
        await RunIndexAsync("publish", nothing, output);

        var record = (await VerifyAsync(output, new InMemoryTrustStore())).Plugins[Samples.PluginId];
        Assert.Equal(VersionState.Held, record.FindVersion(SemanticVersion.Parse("1.1.0"))!.State);
        Assert.Equal(VersionState.Review, record.FindVersion(SemanticVersion.Parse("1.0.0"))!.State);
        Assert.All(record.Versions, version => Assert.Null(version.Package));
        Assert.NotNull(record.FindVersion(SemanticVersion.Parse("1.1.0"))!.HoldUntil);
    }

    [Fact]
    public async Task RefreshesAnExpiredTimestamp()
    {
        var publications = await SetUpRegistryAsync();
        var nothing = _folder.Combine("nothing-deployed");
        Directory.CreateDirectory(nothing);
        _keys.Use(_tool.Environment, OnlineRole.Publish, OnlineRole.Moderation, OnlineRole.Timestamp);
        var first = _folder.Combine("first");
        await RunIndexAsync("publish", nothing, first, "--publications", publications);

        _tool.Clock.Advance(TimeSpan.FromDays(5));
        _keys.Use(_tool.Environment, OnlineRole.Timestamp);
        var refreshed = _folder.Combine("refreshed");
        await RunIndexAsync("refresh", first, refreshed);
        Assert.Equal(2, (await VerifyAsync(refreshed, new InMemoryTrustStore())).Timestamp.Version);
    }

    [Fact]
    public async Task RenewsASnapshotWithin30DaysOfItsExpiry()
    {
        var publications = await SetUpRegistryAsync();
        var nothing = _folder.Combine("nothing-deployed");
        Directory.CreateDirectory(nothing);
        _keys.Use(_tool.Environment, OnlineRole.Publish, OnlineRole.Moderation, OnlineRole.Timestamp);
        var first = _folder.Combine("first");
        await RunIndexAsync("publish", nothing, first, "--publications", publications);

        _tool.Clock.Advance(TimeSpan.FromDays(61));
        var renewed = _folder.Combine("renewed");
        await RunIndexAsync("publish", first, renewed);
        var view = await VerifyAsync(renewed, new InMemoryTrustStore());
        Assert.Equal(2, view.Snapshot.Version);
        Assert.Equal(_tool.Clock.GetUtcNow().AddDays(90), view.Snapshot.Expires);
    }

    [Fact]
    public async Task TellsTheWorkflowWhenItBootstraps()
    {
        await SetUpRegistryAsync();
        var nothing = _folder.Combine("nothing-deployed");
        Directory.CreateDirectory(nothing);
        var outputs = _folder.Combine("github-output");
        _tool.Environment["GITHUB_OUTPUT"] = outputs;
        _keys.Use(_tool.Environment, OnlineRole.Publish);

        await RunIndexAsync("publish", nothing, _folder.Combine("staged"));

        Assert.Equal("bootstrap=true\n", await File.ReadAllTextAsync(outputs, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CarriesTheLicenseTheReportLinkAndTheYankReasonIntoTheIndex()
    {
        var publications = await SetUpRegistryAsync();
        var registry = await _registry.LoadAsync();
        _registry.Version(Samples.PluginId, registry.VersionsOf(Samples.PluginId).Single(record => record.Version == SemanticVersion.Parse("1.0.0")) with
        {
            State = VersionState.Yanked,
            YankReason = "Corrupts settings.",
        });
        var nothing = _folder.Combine("nothing-deployed");
        Directory.CreateDirectory(nothing);
        _keys.Use(_tool.Environment, OnlineRole.Publish, OnlineRole.Moderation, OnlineRole.Timestamp);

        await RunIndexAsync("publish", nothing, _folder.Combine("published"), "--publications", publications);
        var record = (await VerifyAsync(_folder.Combine("published"), new InMemoryTrustStore())).Plugins[Samples.PluginId];

        var yanked = record.FindVersion(SemanticVersion.Parse("1.0.0"))!;
        var current = record.FindVersion(SemanticVersion.Parse("1.1.0"))!;
        Assert.Equal((VersionState.Yanked, "Corrupts settings."), (yanked.State, yanked.YankReason));
        Assert.Null(current.YankReason);
        Assert.All(record.Versions, version => Assert.Equal("MIT", version.License));
        Assert.All(record.Versions, version => Assert.Equal("https://github.com/RunesmithHub/registry/issues/501", version.ReportUrl));
    }

    [Fact]
    public async Task RefusesToPublishWithoutThePublishKey()
    {
        await SetUpRegistryAsync();
        var nothing = _folder.Combine("nothing-deployed");
        Directory.CreateDirectory(nothing);
        _keys.Use(_tool.Environment, OnlineRole.Timestamp);

        var (exitCode, _, error) = await _tool.RunAsync("index", "publish", "--registry", _registry.Path, "--index", nothing);

        Assert.Equal(1, exitCode);
        Assert.Contains("HUB_PUBLISH_KEY", error, StringComparison.Ordinal);
    }
}
