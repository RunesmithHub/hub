using System.Text.Json;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Protocol.Resolution;
using RunesmithHub.Protocol.Updating;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Tests.Support;
using SkiaSharp;

namespace RunesmithHub.Tool.Tests;

public sealed class FixtureTests : IDisposable
{
    private const string BaseUrl = "https://fixture.example/hub/";

    private readonly TemporaryFolder _folder = new();
    private readonly ToolRunner _tool = new();

    public void Dispose() => _folder.Dispose();

    private string Index => _folder.Combine("index");

    private async Task WriteFixtureAsync(params string[] extra)
    {
        var (exitCode, output, error) = await _tool.RunAsync(["dev", "fixture", Index, "--base-url", BaseUrl, .. extra]);
        Assert.True(exitCode == 0, error);
        Assert.Contains("14 plugins", output, StringComparison.Ordinal);
    }

    private async Task<IndexUpdateResult> UpdateAsync(TimeProvider? clock = null)
    {
        var shippedRoot = await File.ReadAllBytesAsync(Path.Combine(Index, "root", "1.json"), TestContext.Current.CancellationToken);
        var updater = new IndexUpdater(new DirectoryIndexFetcher(Index), new InMemoryTrustStore(), shippedRoot, clock ?? _tool.Clock);
        return await updater.UpdateAsync(TestContext.Current.CancellationToken);
    }

    private async Task<IndexView> ViewAsync()
    {
        var result = await UpdateAsync();
        Assert.True(result.Succeeded, result.Failure?.Message);
        return result.View;
    }

    private string LocalPath(string url)
    {
        Assert.StartsWith(BaseUrl, url, StringComparison.Ordinal);
        return Path.Combine(Index, url[BaseUrl.Length..]);
    }

    [Fact]
    public async Task WritesAnIndexThatVerifiesWithTheUpdater()
    {
        await WriteFixtureAsync();

        var view = await ViewAsync();

        Assert.Equal(14, view.Plugins.Count);
        Assert.Equal(11, view.Publishers.Publishers.Count);
        Assert.Equal(3, view.Moderation.Entries.Count);
        Assert.Equal(Categories.Default, view.Policy.Categories);
        Assert.Equal(_tool.Clock.GetUtcNow().AddDays(3), view.ExpiresAt);
    }

    [Fact]
    public async Task UsesTheLayoutAndTiersOfTheDesign()
    {
        await WriteFixtureAsync();

        var view = await ViewAsync();

        Assert.True(File.Exists(Path.Combine(Index, "dashboard.json")));
        Assert.True(File.Exists(Path.Combine(Index, "stats.json")));
        Assert.Single(Directory.GetFiles(Path.Combine(Index, "snapshot")));
        Assert.Single(Directory.GetFiles(Path.Combine(Index, "moderation")));
        Assert.Equal(14, Directory.GetFiles(Path.Combine(Index, "plugins")).Length);
        Assert.Equal(["runesmith.csharp", "runesmith.dutch", "runesmith.git", "runesmith.github"], Ids(view, PluginTier.Official));
        Assert.Equal(["ember.themes", "harbor.issues", "northwind.charts", "tide.formatter"], Ids(view, PluginTier.Verified));
        Assert.Equal(["atlas.rest", "cobalt.docker", "copper.snippets", "fastcode.helper", "lumen.todo", "pixel.icons"], Ids(view, PluginTier.Unverified));
        Assert.All(view.Plugins.Values.SelectMany(p => p.Versions), v => Assert.Equal(">=0.1.0 <0.2.0", v.RunesmithApi.ToString()));

        var lumen = view.Plugins["lumen.todo"].Versions.Single(v => v.Version == SemanticVersion.Parse("1.0.0"));
        Assert.Equal(["harbor.issues >=1.2.0 <2.0.0", "runesmith.git >=0.3.1 <0.4.0"], lumen.Dependencies.Select(d => $"{d.Id} {d.Range}"));
    }

    private static IEnumerable<string> Ids(IndexView view, PluginTier tier) => view.Plugins.Values.Where(p => p.Tier == tier).Select(p => p.Id).Order();

    [Fact]
    public async Task CarriesALocalizedPluginAndALanguagePack()
    {
        await WriteFixtureAsync();

        var view = await ViewAsync();

        Assert.All(view.Plugins["runesmith.git"].Versions, version => Assert.Equal(["en", "nl", "de"], version.Languages));
        Assert.All(view.Plugins.Values.Where(plugin => plugin.Id != "runesmith.git").SelectMany(plugin => plugin.Versions), version => Assert.Null(version.Languages));
        var pack = Assert.Single(view.Plugins["runesmith.dutch"].Versions);
        Assert.Equal(("nl", "Nederlands", "Dutch"), (pack.LanguagePack!.Language, pack.LanguagePack.Name, pack.LanguagePack.EnglishName));
        Assert.Equal(["runesmith", "runesmith.github"], pack.LanguagePack.Targets);
        Assert.Empty(pack.Capabilities.Declared);
        await using var stream = File.OpenRead(LocalPath(pack.Package!.Urls[0]));
        using var archive = PackageArchive.Open(stream);
        Assert.Equal("Dutch", archive.ReadManifest().LanguagePack!.EnglishName);
    }

    [Fact]
    public async Task CarriesTheStatesOfTheDesignExamples()
    {
        await WriteFixtureAsync();
        var catalog = new HubCatalog(await ViewAsync());

        EffectiveState State(string id, string version) => catalog.GetEffectiveState(id, SemanticVersion.Parse(version))!;

        Assert.Equal(VersionState.Blocked, State("harbor.issues", "1.3.1").State);
        Assert.Equal(VersionState.Published, State("harbor.issues", "1.3.2").State);
        Assert.Equal(VersionState.Review, State("harbor.issues", "1.4.0").State);
        Assert.Equal(VersionState.Held, State("lumen.todo", "1.1.0").State);
        Assert.Equal((VersionState.Frozen, 3), (State("pixel.icons", "2.1.3").State, State("pixel.icons", "2.1.3").Moderation!.Id));
        Assert.Equal((VersionState.Blocked, ModerationReason.Malicious), (State("fastcode.helper", "1.0.1").State, State("fastcode.helper", "1.0.1").Moderation!.Reason));
        Assert.Equal(PluginState.Deprecated, catalog.FindPlugin("copper.snippets")!.State);
        Assert.Equal("runesmith.csharp", catalog.FindPlugin("copper.snippets")!.Deprecated!.Replacement);
        Assert.True(State("copper.snippets", "1.2.0").IsInstallable);
    }

    [Fact]
    public async Task CarriesTheLicenseReportLinkAndYankReasonOfEachVersion()
    {
        await WriteFixtureAsync();
        var view = await ViewAsync();

        var yanked = view.Plugins["lumen.todo"].FindVersion(SemanticVersion.Parse("0.9.2"))!;
        Assert.Equal((VersionState.Yanked, "Crashed on empty files."), (yanked.State, yanked.YankReason));
        Assert.Equal("GPL-3.0-only", view.Plugins["atlas.rest"].Versions[0].License);
        Assert.All(view.Plugins.Values.SelectMany(p => p.Versions), v => Assert.Matches("^https://github.com/RunesmithHub/registry/issues/[0-9]+$", v.ReportUrl));
        Assert.All(view.Plugins.Values.SelectMany(p => p.Versions).Where(v => v.State != VersionState.Yanked), v => Assert.Null(v.YankReason));
    }

    [Fact]
    public async Task ResolvesTheWorkedExampleAgainstTheFixture()
    {
        await WriteFixtureAsync();
        var catalog = new HubCatalog(await ViewAsync());
        var host = new RunesmithHost(SemanticVersion.Parse("0.1.0"), SemanticVersion.Parse("0.1.0"), Platforms.Linux);
        var installed = new List<InstalledPlugin> { new("runesmith.git", SemanticVersion.Parse("0.3.0"), Requested: true) };

        var result = new Resolver(catalog, new ResolutionSettings(host)).Resolve(installed, ResolutionRequest.Install("lumen.todo"));

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.Equal(
            [(OperationKind.Update, "runesmith.git", "0.3.4"), (OperationKind.Install, "harbor.issues", "1.3.2"), (OperationKind.Install, "lumen.todo", "1.0.0")],
            result.Plan.Changes.Select(op => (op.Kind, op.PluginId, op.To!.ToString())));
    }

    [Fact]
    public async Task PackagesDownloadAndMatchTheirRecords()
    {
        await WriteFixtureAsync();
        var view = await ViewAsync();
        var checkedPackages = 0;

        foreach (var plugin in view.Plugins.Values)
        {
            foreach (var version in plugin.Versions.Where(v => v.Package is not null))
            {
                var bytes = await File.ReadAllBytesAsync(LocalPath(version.Package!.Urls[0]), TestContext.Current.CancellationToken);
                Assert.Equal(version.Package.Length, bytes.Length);
                Assert.True(Hashes.Matches(bytes, version.Package.Sha256));

                using var archive = PackageArchive.Open(new MemoryStream(bytes));
                var manifest = archive.ReadManifest();
                var staging = _folder.Combine("staging", plugin.Id, version.Version.ToString());
                archive.ExtractTo(staging);

                Assert.Empty(PackageVerifier.CheckAgainstRecord(manifest, plugin.Id, version));
                Assert.Empty(PackageVerifier.CheckAssemblies(staging, manifest));
                Assert.Equal(version.Contracts.Sha256, manifest.Assemblies.Contracts.Sha256);
                checkedPackages++;
            }
        }

        Assert.Equal(23, checkedPackages);
        Assert.Null(view.Plugins["harbor.issues"].FindVersion(SemanticVersion.Parse("1.3.1"))!.Package);
        Assert.Null(view.Plugins["harbor.issues"].FindVersion(SemanticVersion.Parse("1.4.0"))!.Package);
    }

    [Fact]
    public async Task IconsArePngTilesOfTheStatedSizeAndHash()
    {
        await WriteFixtureAsync();
        var view = await ViewAsync();

        foreach (var image in view.Plugins.Values.SelectMany(p => new[] { p.Icon.Size64, p.Icon.Size128, p.Icon.Size256 }))
        {
            var bytes = await File.ReadAllBytesAsync(LocalPath(image.Url), TestContext.Current.CancellationToken);
            using var bitmap = SKBitmap.Decode(bytes);

            Assert.Equal(ImageFormat.Png, image.Format);
            Assert.Equal((image.Width, image.Height), (bitmap.Width, bitmap.Height));
            Assert.Equal(image.Length, bytes.Length);
            Assert.True(Hashes.Matches(bytes, image.Sha256));
            Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
            Assert.Equal(255, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 20).Alpha);
        }
    }

    [Fact]
    public async Task ModeratesARunningFixtureSoAnUpdatingClientSeesTheBlock()
    {
        await WriteFixtureAsync();
        var shippedRoot = await File.ReadAllBytesAsync(Path.Combine(Index, "root", "1.json"), TestContext.Current.CancellationToken);
        var updater = new IndexUpdater(new DirectoryIndexFetcher(Index), new InMemoryTrustStore(), shippedRoot, _tool.Clock);
        Assert.True((await updater.UpdateAsync(TestContext.Current.CancellationToken)).Succeeded);
        _tool.Clock.Advance(TimeSpan.FromHours(1));

        var (exitCode, output, error) = await _tool.RunAsync("dev", "moderate", Index, "--block", "lumen.todo@>=1.0.0 <1.1.0", "--reason", "compromised-account", "--note", "Testing a block.");
        var update = await updater.UpdateAsync(TestContext.Current.CancellationToken);

        Assert.True(exitCode == 0, error);
        Assert.Contains("Added entry 4: block lumen.todo@>=1.0.0 <1.1.0. Moderation list version 2, timestamp version 2.", output, StringComparison.Ordinal);
        Assert.True(update.Succeeded, update.Failure?.Message);
        Assert.Equal((2, 2), (update.View.Moderation.Version, update.View.Timestamp.Version));
        var entry = update.View.Moderation.Entries[^1];
        Assert.Equal((4, ModerationAction.Block, ModerationReason.CompromisedAccount, "Testing a block."), (entry.Id, entry.Action, entry.Reason, entry.Note));
        var catalog = new HubCatalog(update.View);
        Assert.Equal(VersionState.Blocked, catalog.GetEffectiveState("lumen.todo", SemanticVersion.Parse("1.0.0"))!.State);
        Assert.Equal(VersionState.Yanked, catalog.GetEffectiveState("lumen.todo", SemanticVersion.Parse("0.9.2"))!.State);
        Assert.Equal(_tool.Clock.GetUtcNow().AddDays(3), update.View.Timestamp.Expires);
    }

    [Fact]
    public async Task FreezesAPluginWithTheBrokenReasonByDefaultAndRefusesBadInput()
    {
        await WriteFixtureAsync();

        var freeze = await _tool.RunAsync("dev", "moderate", Index, "--freeze", "atlas.rest");
        var both = await _tool.RunAsync("dev", "moderate", Index, "--freeze", "atlas.rest", "--block", "atlas.rest");
        var badReason = await _tool.RunAsync("dev", "moderate", Index, "--block", "publisher:atlas", "--reason", "boredom");
        var notAFixture = await _tool.RunAsync("dev", "moderate", _folder.Combine("nothing"), "--block", "atlas.rest");

        Assert.True(freeze.ExitCode == 0, freeze.Error);
        var view = await ViewAsync();
        Assert.Equal((ModerationAction.Freeze, ModerationReason.Broken), (view.Moderation.Entries[^1].Action, view.Moderation.Entries[^1].Reason));
        Assert.Equal(VersionState.Frozen, new HubCatalog(view).GetEffectiveState("atlas.rest", SemanticVersion.Parse("0.6.0"))!.State);
        Assert.Contains("either --block or --freeze", both.Error, StringComparison.Ordinal);
        Assert.Contains("\"boredom\" is not a reason category", badReason.Error, StringComparison.Ordinal);
        Assert.Contains("is not a fixture", notAFixture.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritesAnExpiredTimestampWhenAsked()
    {
        await WriteFixtureAsync("--expired");

        var result = await UpdateAsync();

        Assert.Equal(IndexUpdateFailureKind.MetadataExpired, result.Failure!.Kind);
        Assert.Contains("timestamp expired", result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritesItsRootChainAndTestKeys()
    {
        await WriteFixtureAsync();

        var verify = await _tool.RunAsync("keys", "root", "verify", Path.Combine(Index, "root"));

        Assert.Equal(0, verify.ExitCode);
        Assert.Contains("Root 1: valid, signed by 2 of its 3 root keys", verify.Output, StringComparison.Ordinal);
        Assert.Equal(
            ["README.txt", "test-moderation.pem", "test-moderation.pub.json", "test-publish.pem", "test-publish.pub.json", "test-root-1.pem", "test-root-1.pub.json",
                "test-root-2.pem", "test-root-2.pub.json", "test-root-3.pem", "test-root-3.pub.json", "test-timestamp.pem", "test-timestamp.pub.json"],
            Directory.GetFiles(Path.Combine(Index, "keys")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task WritesTheDashboardAndStats()
    {
        await WriteFixtureAsync();

        using var dashboard = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(Index, "dashboard.json"), TestContext.Current.CancellationToken));
        using var stats = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(Index, "stats.json"), TestContext.Current.CancellationToken));

        var queue = dashboard.RootElement.GetProperty("reviewQueue");
        Assert.Equal("harbor.issues", queue[0].GetProperty("plugin").GetString());
        Assert.Equal("lumen.todo", dashboard.RootElement.GetProperty("held")[0].GetProperty("plugin").GetString());
        Assert.Equal(3, dashboard.RootElement.GetProperty("actions").GetArrayLength());
        Assert.Equal("freeze", dashboard.RootElement.GetProperty("actions")[0].GetProperty("action").GetString());
        Assert.Equal(51930, stats.RootElement.GetProperty("installs").GetProperty("runesmith.git").GetInt32());
    }

    [Fact]
    public async Task RefusesAFolderThatIsNotEmptyAndABadBaseUrl()
    {
        Directory.CreateDirectory(Index);
        await File.WriteAllTextAsync(Path.Combine(Index, "something.txt"), "x", TestContext.Current.CancellationToken);

        var notEmpty = await _tool.RunAsync("dev", "fixture", Index);
        var badUrl = await _tool.RunAsync("dev", "fixture", _folder.Combine("other"), "--base-url", "ftp://example/");

        Assert.Equal(1, notEmpty.ExitCode);
        Assert.Contains("is not empty", notEmpty.Error, StringComparison.Ordinal);
        Assert.Equal(1, badUrl.ExitCode);
        Assert.Contains("ending in /", badUrl.Error, StringComparison.Ordinal);
    }
}
