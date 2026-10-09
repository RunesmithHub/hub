using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.NuGet;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Build;

public sealed class SharedFrameworksTests : IDisposable
{
    private const string FileName = "System.Data.DataSetExtensions.dll";
    private const string PackageFolder = "system.data.datasetextensions/4.5.0";

    private readonly TemporaryFolder _folder = new();
    private readonly byte[] _assembly;

    public SharedFrameworksTests()
    {
        using var package = NuGetPackage.Open(SignedNuGet.Bytes());
        _assembly = package.ReadFile(SignedNuGet.AssemblyPath)!;
        var restored = _folder.Combine("packages", PackageFolder, SignedNuGet.AssemblyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(restored)!);
        File.WriteAllBytes(restored, _assembly);
        Directory.CreateDirectory(_folder.Combine("bin"));
    }

    public void Dispose() => _folder.Dispose();

    private string DotnetRoot(string packVersion, string assemblyVersion, string overrides = "")
    {
        var data = Directory.CreateDirectory(_folder.Combine("dotnet", "packs", "Microsoft.NETCore.App.Ref", packVersion, "data")).FullName;
        File.WriteAllText(Path.Combine(data, "PlatformManifest.txt"), $"{FileName}|Microsoft.NETCore.App.Ref|{assemblyVersion}|10.0.1226.42308\nlibSystem.Native.so|Microsoft.NETCore.App.Ref||0.0.0.0\n");
        File.WriteAllText(Path.Combine(data, "PackageOverrides.txt"), overrides);
        return _folder.Combine("dotnet");
    }

    private AssetsFile Assets() =>
        new("net10.0", [new AssetLibrary(SignedNuGet.Id, SignedNuGet.Version, false, [SignedNuGet.AssemblyPath], []) { PackageFolder = PackageFolder }])
        {
            PackageFolders = [_folder.Combine("packages")],
            FrameworkReferences = ["Microsoft.NETCore.App"],
        };

    private (IReadOnlyList<BundleItem> Items, List<string> Problems) Collect(string dotnetRoot)
    {
        var assets = Assets();
        var problems = new List<string>();
        var items = BundleCollector.Collect(new ProjectOutput("Lumen.Todo", _folder.Combine("bin"), assets), "Lumen.Todo.Contracts", SharedFrameworks.Locate(assets, [dotnetRoot]), problems);
        return (items, problems);
    }

    [Fact]
    public void SkipsAPackageAssetTheBuildLeftOutBecauseTheFrameworksCopyWins()
    {
        var (items, problems) = Collect(DotnetRoot("10.0.3", "10.0.0.0"));

        Assert.Empty(items);
        Assert.Empty(problems);
    }

    [Fact]
    public void ReportsAMissingAssetWhenThePackagesCopyWouldWin()
    {
        var (_, problems) = Collect(DotnetRoot("10.0.3", "1.0.0.0"));

        Assert.Equal([$"{FileName} from {SignedNuGet.Id} {SignedNuGet.Version} is not in the build output; set EnableDynamicLoading to true in the implementation project so the build copies it."], problems);
    }

    [Fact]
    public void LetsThePackageOverridesDecideBeforeTheVersions()
    {
        var (_, problems) = Collect(DotnetRoot("10.0.3", "1.0.0.0", $"{SignedNuGet.Id}|4.5.0\nOther.Package|1.0.0\n"));

        Assert.Empty(problems);
    }

    [Fact]
    public void ReadsOnlyTheTargetingPackOfTheProjectsDotnetVersion()
    {
        var (_, problems) = Collect(DotnetRoot("9.0.8", "10.0.0.0"));

        Assert.Single(problems);
    }

    [Fact]
    public void BundlesAndVerifiesAnAssetTheBuildCopiedEvenWhenTheFrameworkHasOne()
    {
        File.WriteAllBytes(_folder.Combine("bin", FileName), _assembly);

        var (items, problems) = Collect(DotnetRoot("10.0.3", "10.0.0.0"));

        Assert.Empty(problems);
        Assert.Equal((SignedNuGet.Id, SignedNuGet.AssemblyPath), (Assert.Single(items).PackageId, items[0].PathInPackage));
    }

    [Fact]
    public void SuppliesNothingWithoutATargetingPack()
    {
        var frameworks = SharedFrameworks.Locate(Assets(), [_folder.Combine("no-dotnet")]);

        Assert.False(frameworks.Supplies(SignedNuGet.Id, SignedNuGet.Version, FileName, _folder.Combine("packages", PackageFolder, SignedNuGet.AssemblyPath)));
    }

    [Fact]
    public void ReadsThePackageFoldersAndFrameworkReferencesFromTheAssetsFile()
    {
        var path = _folder.Combine(AssetsFile.FileName);
        File.WriteAllText(path, """
            {
              "version": 3,
              "targets": { "net10.0": { "System.Data.DataSetExtensions/4.5.0": { "type": "package", "runtime": { "lib/netstandard2.0/System.Data.DataSetExtensions.dll": {} } } } },
              "libraries": { "System.Data.DataSetExtensions/4.5.0": { "type": "package", "path": "system.data.datasetextensions/4.5.0" } },
              "packageFolders": { "/packages/": {} },
              "project": { "frameworks": { "net10.0": { "frameworkReferences": { "Microsoft.NETCore.App": { "privateAssets": "all" } } } } }
            }
            """);

        var assets = AssetsFile.Read(path);

        Assert.Equal(["/packages/"], assets.PackageFolders);
        Assert.Equal(["Microsoft.NETCore.App"], assets.FrameworkReferences);
        Assert.Equal(PackageFolder, Assert.Single(assets.Libraries).PackageFolder);
    }
}
