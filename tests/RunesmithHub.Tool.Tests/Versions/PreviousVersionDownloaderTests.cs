using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Tests.Support;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Tests.Versions;

public sealed class PreviousVersionDownloaderTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();
    private readonly FakeDownloader _downloads = new();

    public void Dispose() => _folder.Dispose();

    private HostedFile Hosted(string version, byte[] content)
    {
        var url = $"https://example.com/lumen.todo-{version}.rsplugin";
        _downloads.Files[url] = content;
        return new HostedFile { Urls = [url], Sha256 = Hashes.Sha256(content), Length = content.Length };
    }

    private static HubCatalog Catalog(params VersionRecord[] versions) => new(
        [DependencyChecksTests.Plugin(Samples.PluginId, PluginTier.Unverified, versions)],
        new PublisherList { Type = PayloadTypes.Publishers, Version = 1, Publishers = [] },
        []);

    private Task<string?> DownloadAsync(HubCatalog catalog, string version) =>
        new PreviousVersionDownloader(_downloads).DownloadAsync(catalog, Samples.PluginId, SemanticVersion.Parse(version), _folder.Path, TestContext.Current.CancellationToken);

    [Fact]
    public async Task DownloadsTheHighestLowerVersionTheIndexStillHosts()
    {
        var catalog = Catalog(
            DependencyChecksTests.Version("1.0.0") with { Package = Hosted("1.0.0", [1]) },
            DependencyChecksTests.Version("1.1.0", VersionState.Blocked) with { Package = Hosted("1.1.0", [2]) },
            DependencyChecksTests.Version("1.2.0", VersionState.Review),
            DependencyChecksTests.Version("2.0.0") with { Package = Hosted("2.0.0", [3]) });

        var path = await DownloadAsync(catalog, "1.5.0");

        Assert.Equal("lumen.todo-1.0.0.rsplugin", Path.GetFileName(path));
        Assert.Equal([1], await File.ReadAllBytesAsync(path!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReturnsNothingForAFirstVersion() => Assert.Null(await DownloadAsync(Catalog(), "1.0.0"));

    [Fact]
    public async Task RefusesAPackageThatDoesNotMatchTheIndex()
    {
        var catalog = Catalog(DependencyChecksTests.Version("1.0.0") with { Package = Hosted("1.0.0", [1]) });
        _downloads.Files["https://example.com/lumen.todo-1.0.0.rsplugin"] = [9];

        var exception = await Assert.ThrowsAsync<ToolException>(() => DownloadAsync(catalog, "1.1.0"));

        Assert.Contains("does not have the hash", exception.Message, StringComparison.Ordinal);
    }
}
