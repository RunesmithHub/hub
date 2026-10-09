using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.NuGet;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Build;

public sealed class ContentFilesTests : IDisposable
{
    private static readonly byte[] Host = [1, 2, 3];
    private static readonly byte[] Settings = "{}"u8.ToArray();

    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static readonly ContentAsset[] Content =
    [
        new("contentFiles/any/any/BuildHost/Host.dll", "BuildHost/Host.dll"),
        new("contentFiles/any/any/BuildHost/Host.runtimeconfig.json", "BuildHost/Host.runtimeconfig.json"),
        new("contentFiles/any/any/Unused.txt", "Unused.txt"),
    ];

    private ProjectOutput Output(params ContentAsset[] content)
    {
        var directory = _folder.Combine("bin");
        Directory.CreateDirectory(Path.Combine(directory, "BuildHost"));
        File.WriteAllBytes(Path.Combine(directory, "BuildHost", "Host.dll"), Host);
        File.WriteAllBytes(Path.Combine(directory, "BuildHost", "Host.runtimeconfig.json"), Settings);
        AssetLibrary[] libraries = [new("Sample.Host", "1.0.0", false, [], []) { ContentFiles = content }];
        return new ProjectOutput("Lumen.Todo", directory, new AssetsFile("net10.0", libraries));
    }

    private static IReadOnlyList<BundleItem> Collect(ProjectOutput output, bool declaresProcess, List<string> problems) =>
        BundleCollector.Collect(output, "Lumen.Todo.Contracts", SharedFrameworks.None, declaresProcess, problems);

    [Fact]
    public void ReadsOnlyTheContentFilesTheBuildCopiesToItsOutput()
    {
        var path = _folder.Combine(AssetsFile.FileName);
        File.WriteAllText(path, """
            {
              "targets": {
                "net10.0": {
                  "Sample.Host/1.0.0": {
                    "type": "package",
                    "contentFiles": {
                      "contentFiles/any/any/BuildHost/Host.dll": { "buildAction": "Content", "copyToOutput": true, "outputPath": "BuildHost/Host.dll" },
                      "contentFiles/cs/any/Source.cs": { "buildAction": "Compile", "copyToOutput": false },
                      "contentFiles/any/any/_._": { "buildAction": "None", "copyToOutput": true, "outputPath": "_._" }
                    }
                  }
                }
              },
              "project": { "frameworks": { "net10.0": {} } }
            }
            """);

        var library = Assert.Single(AssetsFile.Read(path).Libraries);

        Assert.Equal([new ContentAsset("contentFiles/any/any/BuildHost/Host.dll", "BuildHost/Host.dll")], library.ContentFiles);
    }

    [Fact]
    public void BundlesTheContentFilesTheBuildCopiedUnderLibInTheirFolders()
    {
        var problems = new List<string>();

        var items = Collect(Output(Content), declaresProcess: true, problems);

        Assert.Empty(problems);
        Assert.Equal(
            [("lib/BuildHost/Host.dll", "contentFiles/any/any/BuildHost/Host.dll"), ("lib/BuildHost/Host.runtimeconfig.json", "contentFiles/any/any/BuildHost/Host.runtimeconfig.json")],
            items.Select(item => (item.PackagePath, item.PathInPackage!)));
        Assert.All(items, item => Assert.Equal(("Sample.Host", "1.0.0"), (item.PackageId!, item.PackageVersion!)));
    }

    [Fact]
    public void BundlesProgramFilesOnlyWhenTheManifestDeclaresProcess()
    {
        var problems = new List<string>();

        var items = Collect(Output(Content), declaresProcess: false, problems);

        Assert.Equal(["lib/BuildHost/Host.dll"], items.Select(item => item.PackagePath));
        var problem = Assert.Single(problems);
        Assert.StartsWith("BuildHost/Host.runtimeconfig.json from Sample.Host 1.0.0 can't be bundled:", problem, StringComparison.Ordinal);
        Assert.Contains("process capability", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../Host.dll")]
    [InlineData("/etc/Host.dll")]
    public void RejectsContentCopiedOutsideTheOutputFolder(string outputPath)
    {
        var problems = new List<string>();

        var items = Collect(Output(new ContentAsset("contentFiles/any/any/Host.dll", outputPath)), declaresProcess: true, problems);

        Assert.Empty(items);
        Assert.Contains("not a safe path", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsContentInTheFolderForNativeLibraries()
    {
        var output = Output(new ContentAsset("contentFiles/any/any/native/linux-x64/libx.so", "native/linux-x64/libx.so"));
        Directory.CreateDirectory(Path.Combine(output.Directory, "native", "linux-x64"));
        File.WriteAllBytes(Path.Combine(output.Directory, "native", "linux-x64", "libx.so"), Host);
        var problems = new List<string>();

        var items = Collect(output, declaresProcess: true, problems);

        Assert.Empty(items);
        Assert.Contains("native runtime assets", Assert.Single(problems), StringComparison.Ordinal);
    }

    private async Task<List<string>> VerifyAsync(byte[] publishedHost)
    {
        var problems = new List<string>();
        var items = Collect(Output(Content), declaresProcess: true, problems);
        var package = TestNuGet.Package("Sample.Host", "1.0.0", "MIT",
            ("contentFiles/any/any/BuildHost/Host.dll", publishedHost), ("contentFiles/any/any/BuildHost/Host.runtimeconfig.json", Settings));
        var cache = Directory.CreateDirectory(_folder.Combine("cache")).FullName;
        File.WriteAllBytes(Path.Combine(cache, "sample.host.1.0.0.nupkg"), package);
        await new BundleVerifier(new FolderNuGetPackageSource(cache, null)).VerifyAsync(
            items, [new LockedPackage("Sample.Host", "1.0.0", TestNuGet.ContentHash(package))], problems, TestContext.Current.CancellationToken);
        return problems;
    }

    [Fact]
    public async Task VerifiesContentFilesAgainstTheLockedPackage() => Assert.Empty(await VerifyAsync(Host));

    [Fact]
    public async Task RejectsAContentFileWithOneByteChanged()
    {
        var problems = await VerifyAsync([1, 2, 4]);

        Assert.Equal(["BuildHost/Host.dll in the build output differs from contentFiles/any/any/BuildHost/Host.dll in Sample.Host 1.0.0."], problems);
    }
}
