using System.Text;
using RunesmithHub.Tool.NuGet;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Build;

public sealed class NuGetTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void ReadsEveryLockedPackageOnceWithoutProjects()
    {
        var path = _folder.Combine(LockFile.FileName);
        File.WriteAllText(path, """
            {
              "version": 1,
              "dependencies": {
                "net10.0": {
                  "Markdig": { "type": "Direct", "requested": "[0.40.0, )", "resolved": "0.40.0", "contentHash": "bWFya2RpZw==" },
                  "Lumen.Todo.Contracts": { "type": "Project" },
                  "System.Memory": { "type": "Transitive", "resolved": "4.6.0", "contentHash": "bWVtb3J5" }
                },
                "net10.0/linux-x64": {
                  "Markdig": { "type": "Direct", "requested": "[0.40.0, )", "resolved": "0.40.0", "contentHash": "bWFya2RpZw==" }
                }
              }
            }
            """);

        var packages = LockFile.Read(path);

        Assert.Equal([new LockedPackage("Markdig", "0.40.0", "bWFya2RpZw=="), new LockedPackage("System.Memory", "4.6.0", "bWVtb3J5")], packages);
    }

    [Fact]
    public void RejectsALockFileWithoutDependencies()
    {
        var path = _folder.Combine(LockFile.FileName);
        File.WriteAllText(path, "{}");

        Assert.Throws<ToolException>(() => LockFile.Read(path));
    }

    [Fact]
    public void ReadsTheRuntimeAndNativeAssetsOfEachLibrary()
    {
        var path = _folder.Combine(AssetsFile.FileName);
        File.WriteAllText(path, """
            {
              "version": 3,
              "targets": {
                "net10.0": {
                  "Markdig/0.40.0": { "type": "package", "runtime": { "lib/net8.0/Markdig.dll": { "related": ".xml" } } },
                  "LibGit2Sharp.NativeBinaries/2.0.0": {
                    "type": "package",
                    "runtime": { "lib/net8.0/_._": {} },
                    "runtimeTargets": {
                      "runtimes/linux-x64/native/libgit2.so": { "assetType": "native", "rid": "linux-x64" },
                      "runtimes/linux-x64/lib/net8.0/Other.dll": { "assetType": "runtime", "rid": "linux-x64" }
                    }
                  },
                  "Lumen.Helpers/1.0.0": { "type": "project", "runtime": { "bin/placeholder/Lumen.Helpers.dll": {} } }
                }
              },
              "project": { "frameworks": { "net10.0": {} } }
            }
            """);

        var assets = AssetsFile.Read(path);

        Assert.Equal("net10.0", assets.TargetFramework);
        Assert.Equal(["lib/net8.0/Markdig.dll"], assets.Libraries[0].RuntimeAssemblies);
        Assert.Empty(assets.Libraries[1].RuntimeAssemblies);
        Assert.Equal([new NativeAsset("runtimes/linux-x64/native/libgit2.so", "linux-x64")], assets.Libraries[1].NativeAssets);
        Assert.True(assets.Libraries[2].IsProject);
        Assert.Equal("Lumen.Helpers", assets.Libraries[2].Name);
    }

    [Fact]
    public void RejectsMoreThanOneTargetFramework()
    {
        var path = _folder.Combine(AssetsFile.FileName);
        File.WriteAllText(path, """{ "targets": { "net8.0": {}, "net10.0": {} }, "project": { "frameworks": { "net8.0": {}, "net10.0": {} } } }""");

        var exception = Assert.Throws<ToolException>(() => AssetsFile.Read(path));

        Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAPackagesHashLicenseAndFiles()
    {
        var bytes = TestNuGet.Package("Markdig", "0.40.0", "BSD-2-Clause", ("lib/net8.0/Markdig.dll", [1, 2, 3]));

        using var package = NuGetPackage.Open(bytes);

        Assert.Equal(TestNuGet.ContentHash(bytes), package.ContentHash);
        Assert.Equal("BSD-2-Clause", package.License.Expression);
        Assert.Equal("Copyright Someone", package.Copyright);
        Assert.Equal("https://example.com/Markdig", package.ProjectUrl);
        Assert.Equal([1, 2, 3], package.ReadFile("lib/net8.0/Markdig.dll"));
        Assert.Null(package.ReadFile("lib/net8.0/Other.dll"));
    }

    [Fact]
    public void ComputesTheContentHashOfASignedPackageWithoutItsSignature()
    {
        var bytes = SignedNuGet.Bytes();

        using var package = NuGetPackage.Open(bytes);

        Assert.Equal(SignedNuGet.ContentHash, package.ContentHash);
        Assert.NotEqual(TestNuGet.ContentHash(bytes), package.ContentHash);
    }

    [Fact]
    public void RejectsBytesThatAreNotAPackage() => Assert.Throws<ToolException>(() => NuGetPackage.Open("not a zip"u8.ToArray()));

    [Fact]
    public async Task ReadsPackagesFromTheGlobalPackagesLayoutOrAFlatFolder()
    {
        Directory.CreateDirectory(_folder.Combine("markdig", "0.40.0"));
        File.WriteAllBytes(_folder.Combine("markdig", "0.40.0", "markdig.0.40.0.nupkg"), [1]);
        File.WriteAllBytes(_folder.Combine("system.memory.4.6.0.nupkg"), [2]);
        var source = new FolderNuGetPackageSource(_folder.Path, null);

        Assert.Equal([1], await source.GetAsync("Markdig", "0.40.0", TestContext.Current.CancellationToken));
        Assert.Equal([2], await source.GetAsync("System.Memory", "4.6.0", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ToolException>(() => source.GetAsync("Other", "1.0.0", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadsThirdPartyPackagesFromNuGetOrgAndHubPackagesFromTheHubFeed()
    {
        using var http = new FakeHttp()
            .Answer("https://api.nuget.org/v3-flatcontainer/markdig/0.40.0/markdig.0.40.0.nupkg", [1])
            .Answer("https://feed.example/index.json", Encoding.UTF8.GetBytes("""{ "resources": [{ "@id": "https://feed.example/flat", "@type": "PackageBaseAddress/3.0.0" }] }"""))
            .Answer("https://feed.example/flat/runesmith.sdk/0.1.0/runesmith.sdk.0.1.0.nupkg", [2]);
        using var client = new HttpClient(http);
        var source = new HttpNuGetPackageSource(client, new Uri("https://feed.example/index.json"));

        Assert.Equal([1], await source.GetAsync("Markdig", "0.40.0", TestContext.Current.CancellationToken));
        Assert.Equal([2], await source.GetAsync("Runesmith.Sdk", "0.1.0", TestContext.Current.CancellationToken));
        var missing = await Assert.ThrowsAsync<ToolException>(() => source.GetAsync("Missing", "1.0.0", TestContext.Current.CancellationToken));
        Assert.Contains("is not on api.nuget.org", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapsTheRunesmithPrefixesAndHammerUIToTheHubFeed()
    {
        Assert.True(NuGetFeeds.IsHubPackage("Runesmith.Sdk"));
        Assert.True(NuGetFeeds.IsHubPackage("RunesmithHub.Lumen.Todo.Contracts"));
        Assert.True(NuGetFeeds.IsHubPackage("HammerUI"));
        Assert.False(NuGetFeeds.IsHubPackage("HammerUI.Extras"));
        Assert.False(NuGetFeeds.IsHubPackage("RunesmithTools"));
        Assert.True(NuGetFeeds.IsContractsPackage("RunesmithHub.Lumen.Todo.Contracts"));
        Assert.False(NuGetFeeds.IsContractsPackage("RunesmithHub.Protocol"));
        Assert.Throws<ToolException>(() => NuGetFeeds.HubFeed("http://insecure.example/index.json", _ => null));
        Assert.Equal("https://other.example/index.json", NuGetFeeds.HubFeed(null, name => name == NuGetFeeds.HubFeedVariable ? "https://other.example/index.json" : null).AbsoluteUri);
    }
}
