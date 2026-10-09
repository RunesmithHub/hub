using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.NuGet;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Build;

public sealed class BundleTests : IDisposable
{
    private static readonly byte[] Markdig = [1, 2, 3];
    private static readonly byte[] LibGit2 = [4, 5];

    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private ProjectOutput Output(params string[] files)
    {
        var directory = _folder.Combine("bin");
        foreach (var file in files)
        {
            var path = Path.Combine(directory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, file.EndsWith("Markdig.dll", StringComparison.Ordinal) ? Markdig : file.EndsWith("libgit2.so", StringComparison.Ordinal) ? LibGit2 : [0]);
        }

        AssetLibrary[] libraries =
        [
            new("Markdig", "0.40.0", false, ["lib/net8.0/Markdig.dll"], []),
            new("LibGit2Sharp.NativeBinaries", "2.0.0", false, [], [new NativeAsset("runtimes/linux-x64/native/libgit2.so", "linux-x64")]),
            new("Lumen.Todo.Contracts", "1.0.0", true, ["bin/placeholder/Lumen.Todo.Contracts.dll"], []),
            new("Lumen.Helpers", "1.0.0", true, ["bin/placeholder/Lumen.Helpers.dll"], []),
            new("Runesmith.Sdk", "0.1.0", false, ["lib/net10.0/Runesmith.Sdk.dll"], []),
            new("RunesmithHub.Harbor.Issues.Contracts", "1.2.0", false, ["lib/net10.0/Harbor.Issues.Contracts.dll"], []),
        ];
        return new ProjectOutput("Lumen.Todo", directory, new AssetsFile("net10.0", libraries));
    }

    private static string[] All => ["Lumen.Todo.dll", "Lumen.Todo.Contracts.dll", "Markdig.dll", "Lumen.Helpers.dll", "Runesmith.Sdk.dll", "Harbor.Issues.Contracts.dll", "runtimes/linux-x64/native/libgit2.so"];

    [Fact]
    public void BundlesPackagesAndProjectsButNotHostAssembliesOrOtherPluginsContracts()
    {
        var problems = new List<string>();

        var items = BundleCollector.Collect(Output(All), "Lumen.Todo.Contracts", problems);

        Assert.Empty(problems);
        Assert.Equal(["lib/Lumen.Helpers.dll", "lib/Markdig.dll", "lib/native/linux-x64/libgit2.so"], items.Select(item => item.PackagePath));
        Assert.Null(items[0].PackageId);
        Assert.Equal(("Markdig", "0.40.0", "lib/net8.0/Markdig.dll"), (items[1].PackageId, items[1].PackageVersion, items[1].PathInPackage));
        Assert.True(items[2].IsNative);
    }

    [Fact]
    public void ReportsAssembliesOfUnknownOriginAndPackagesMissingFromTheOutput()
    {
        var problems = new List<string>();

        BundleCollector.Collect(Output([.. All.Where(file => file != "Markdig.dll"), "Sneaky.dll"]), "Lumen.Todo.Contracts", problems);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, problem => problem.StartsWith("Markdig.dll from Markdig 0.40.0 is not in the build output; set EnableDynamicLoading", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("Sneaky.dll in the build output comes from neither", StringComparison.Ordinal));
    }

    private async Task<(IReadOnlyList<BundledPackage> Packages, List<string> Problems)> VerifyAsync(byte[] package, string lockedHash)
    {
        var problems = new List<string>();
        var items = BundleCollector.Collect(Output(All), "Lumen.Todo.Contracts", problems).Where(item => item.PackageId == "Markdig").ToList();
        Directory.CreateDirectory(_folder.Combine("cache"));
        File.WriteAllBytes(_folder.Combine("cache", "markdig.0.40.0.nupkg"), package);
        var verifier = new BundleVerifier(new FolderNuGetPackageSource(_folder.Combine("cache"), null));
        var packages = await verifier.VerifyAsync(items, [new LockedPackage("Markdig", "0.40.0", lockedHash)], problems, TestContext.Current.CancellationToken);
        return (packages, problems);
    }

    [Fact]
    public async Task AcceptsAnAssemblyThatMatchesItsLockedPackage()
    {
        var package = TestNuGet.Package("Markdig", "0.40.0", "BSD-2-Clause", ("lib/net8.0/Markdig.dll", Markdig));

        var (packages, problems) = await VerifyAsync(package, TestNuGet.ContentHash(package));

        Assert.Empty(problems);
        var verified = Assert.Single(packages);
        Assert.Equal("BSD-2-Clause", verified.License.Expression);
    }

    [Fact]
    public async Task RejectsAPackageWithAnotherHashThanTheLockFile()
    {
        var package = TestNuGet.Package("Markdig", "0.40.0", "BSD-2-Clause", ("lib/net8.0/Markdig.dll", Markdig));

        var (_, problems) = await VerifyAsync(package, TestNuGet.ContentHash([9]));

        Assert.Equal(["Markdig 0.40.0 from the feed does not have the SHA-512 its lock file records."], problems);
    }

    [Fact]
    public async Task RejectsAnAssemblyThatDiffersFromThePackages()
    {
        var package = TestNuGet.Package("Markdig", "0.40.0", "BSD-2-Clause", ("lib/net8.0/Markdig.dll", [7, 7, 7]));

        var (_, problems) = await VerifyAsync(package, TestNuGet.ContentHash(package));

        Assert.Equal(["Markdig.dll in the build output differs from lib/net8.0/Markdig.dll in Markdig 0.40.0."], problems);
    }
}
