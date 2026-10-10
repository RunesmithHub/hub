using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Heuristics;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.NuGet;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Analysis;

public sealed class PluginAnalyzerTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();
    private readonly FakeVulnerabilities _vulnerabilities = new();

    public void Dispose() => _folder.Dispose();

    private string Write(TestPackage package) => package.Write(_folder.Combine($"{Guid.NewGuid():N}.rsplugin"));

    private Task<AnalysisResult> AnalyseAsync(TestPackage package, TestPackage? previous = null, PluginTier tier = PluginTier.Unverified, HeuristicRuleSet? rules = null) =>
        AnalyseAsync(package, package.WriteSource(_folder.Combine($"{Guid.NewGuid():N}")), package.PackageSource(), previous, tier, rules);

    private Task<AnalysisResult> AnalyseAsync(
        TestPackage package, AnalysisSource source, INuGetPackageSource packages, TestPackage? previous = null, PluginTier tier = PluginTier.Unverified, HeuristicRuleSet? rules = null) =>
        new PluginAnalyzer(_vulnerabilities, packages, TimeProvider.System).AnalyseAsync(
            new AnalysisRequest(Write(package), previous is null ? null : Write(previous), TestRegistrations.For("lumen.todo", TestPackage.Repository, tier), source, rules),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task PassesAnUnchangedVersionOfAnUnverifiedPlugin()
    {
        var result = await AnalyseAsync(new TestPackage { Version = "1.0.1" }, new TestPackage());

        Assert.Equal(AnalysisOutcome.Passed, result.Report.Verdict);
        Assert.Empty(result.Report.Triggers);
        Assert.Empty(result.Report.Problems);
        Assert.Equal(HeuristicsState.Skipped, result.Report.Heuristics);
        Assert.Equal(new PreviousPackage(Protocol.Versioning.SemanticVersion.Parse("1.0.0"), new string('a', 40), result.Report.Previous!.Length), result.Report.Previous);
        Assert.Empty(result.Report.Api!.Breaking);
    }

    [Fact]
    public async Task ReviewsTheFirstVersionAndEveryVersionOfVerifiedPlugins()
    {
        Assert.Equal(["first-version"], (await AnalyseAsync(new TestPackage())).Report.Triggers);
        Assert.Equal(["tier"], (await AnalyseAsync(new TestPackage { Version = "1.0.1" }, new TestPackage(), PluginTier.Verified)).Report.Triggers);
    }

    [Fact]
    public async Task ReviewsAddedCapabilitiesDynamicCodeNewPackagesAndRenames()
    {
        var current = new TestPackage
        {
            Version = "1.1.0",
            Name = "Lumen Todos",
            ImplementationSource = """
                namespace Lumen.Todo
                {
                    public sealed class TodoService : Lumen.Todo.Contracts.ITodoService
                    {
                        public int Count => System.Reflection.Assembly.LoadFrom("x.dll").GetTypes().Length;
                    }
                }
                """,
            Capabilities = [("dynamic-code", "Loads language packs.")],
            Bundled = [("Markdig", MarkdigImage, "Markdig", "0.40.0")],
            Locked = [("Markdig", "0.40.0", "BSD-2-Clause")],
        };

        var report = (await AnalyseAsync(current, new TestPackage())).Report;

        Assert.Equal(AnalysisOutcome.Review, report.Verdict);
        Assert.Equal(["capability-added", "dynamic-code", "new-nuget-package", "name-changed"], report.Triggers);
        Assert.Equal(["dynamic-code"], report.Capabilities!.Added);
        Assert.Contains(new DependencyChange("package", "Markdig", "added", null, "0.40.0"), report.Dependencies!.Changes);
    }

    [Fact]
    public async Task SaysNothingOfNewPackagesWhenTheVersionBundlesNone()
    {
        var result = await AnalyseAsync(new TestPackage { Locked = [("Runesmith.Sdk", "0.1.0", null), ("Microsoft.CodeAnalysis.BannedApiAnalyzers", "5.6.0", "MIT")] });

        Assert.Equal(["first-version"], result.Report.Triggers);
        Assert.DoesNotContain("NuGet package that", SummaryWriter.Write(result.Report), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewsAPackageBundledForTheFirstTime()
    {
        var previous = new TestPackage { Locked = [("Markdig", "0.40.0", "BSD-2-Clause")] };
        var current = previous with { Version = "1.0.1", Bundled = [("Markdig", MarkdigImage, "Markdig", "0.40.0")] };

        var result = await AnalyseAsync(current, previous);

        Assert.Equal(["new-nuget-package"], result.Report.Triggers);
        Assert.Contains("- It bundles a NuGet package that no earlier version bundled.\n", SummaryWriter.Write(result.Report), StringComparison.Ordinal);
        Assert.Equal(["first-version", "new-nuget-package"], (await AnalyseAsync(current with { Version = "1.0.0" })).Report.Triggers);
    }

    [Fact]
    public async Task FailsABreakingContractsChangeInTheSameLine()
    {
        var current = new TestPackage
        {
            Version = "1.1.0",
            ContractsSource = "namespace Lumen.Todo.Contracts { public interface ITodoService { } }",
            ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService : Lumen.Todo.Contracts.ITodoService { } }",
        };

        var report = (await AnalyseAsync(current, new TestPackage())).Report;

        Assert.Equal(AnalysisOutcome.Failed, report.Verdict);
        Assert.Contains(report.Problems, problem => problem.Code == ProblemCodes.ApiBreaking && problem.Message.Contains("release it as 2.0.0", StringComparison.Ordinal));
        Assert.Equal("Lumen.Todo.Contracts.ITodoService.Count", Assert.Single(report.Api!.Breaking).Member);
    }

    [Fact]
    public async Task ReportsDependenciesThatMoveToAnotherLine()
    {
        var report = (await AnalyseAsync(new TestPackage { Version = "1.0.1", Dependencies = [("harbor.issues", "^2.0.0")] }, new TestPackage { Dependencies = [("harbor.issues", "^1.2.0")] })).Report;

        Assert.Equal([new DependencyChange("plugin", "harbor.issues", "changed", ">=1.2.0 <2.0.0", ">=2.0.0 <3.0.0")], report.Dependencies!.Changes);
    }

    [Fact]
    public async Task FailsKnownVulnerabilities()
    {
        _vulnerabilities.Advisories["pkg:nuget/Newtonsoft.Json@12.0.1"] = ["GHSA-5crp-9r3c-p9vr"];

        var report = (await AnalyseAsync(new TestPackage { Locked = [("Newtonsoft.Json", "12.0.1", "MIT"), ("Markdig", "0.40.0", null)] })).Report;

        Assert.Equal(AnalysisOutcome.Failed, report.Verdict);
        Assert.Equal(["pkg:nuget/Markdig@0.40.0", "pkg:nuget/Newtonsoft.Json@12.0.1"], _vulnerabilities.Asked);
        Assert.Contains(report.Problems, problem => problem.Code == ProblemCodes.Vulnerability && problem.Message.Contains("GHSA-5crp-9r3c-p9vr", StringComparison.Ordinal));
    }

    private static readonly byte[] MarkdigImage = Compiler.Compile("Markdig", "namespace Markdig { public sealed class Markdown { } }");

    private static TestPackage WithBundledPackages => new()
    {
        Bundled = [("Markdig", MarkdigImage, "Markdig", "0.40.0")],
        Native = [("lib/native/linux-x64/libsample.so", [7, 7, 7], "Sample.Native")],
        Locked = [("Markdig", "0.40.0", "BSD-2-Clause"), ("Sample.Native", "1.0.0", "MIT"), ("Runesmith.Sdk", "0.1.0", null)],
        Capabilities = [("native", "Uses a native library.")],
    };

    [Fact]
    public async Task VerifiesBundledFilesAgainstTheLockedPackagesAndWritesTheSbomFromThem()
    {
        var package = WithBundledPackages;
        var source = package.WriteSource(_folder.Combine("source"));
        var packages = package.PackageSource();

        var result = await AnalyseAsync(package, source, packages);

        Assert.DoesNotContain(result.Report.Problems, problem => problem.Severity == ProblemSeverity.Error);
        Assert.Equal(["Markdig 0.40.0", "Runesmith.Sdk 0.1.0", "Sample.Native 1.0.0"], packages.Requested);
        Assert.Equal(
            [new ReportedPackage("Markdig", "0.40.0", true, "BSD-2-Clause"), new ReportedPackage("Runesmith.Sdk", "0.1.0", false, null), new ReportedPackage("Sample.Native", "1.0.0", true, "MIT")],
            result.Report.Dependencies!.Packages);
        using var written = PackageSnapshot.Open(Write(package), PackageLimits.Default);
        Assert.Equal(written.ReadFile(CycloneDxSbom.FileName), result.Sbom);
        Assert.Equal(new ProducedFile(Protocol.Hashes.Sha256(result.Sbom!), result.Sbom!.Length), result.Report.Sbom);
    }

    [Fact]
    public async Task FailsABundledAssemblyTheLockFilesDoNotPin()
    {
        var report = (await AnalyseAsync(new TestPackage { Bundled = [("Markdig", MarkdigImage, "Markdig", "0.40.0")] })).Report;

        var problem = Assert.Single(report.Problems, problem => problem.Code == ProblemCodes.BundledProvenance);
        Assert.Equal("lib/Markdig.dll names Markdig 0.40.0, which the lock files do not pin.", problem.Message);
    }

    [Fact]
    public async Task FailsABundledAssemblyThatIsNotTheFileOfItsPackage()
    {
        var package = WithBundledPackages with { PublishedImages = new Dictionary<string, byte[]> { ["Markdig"] = Compiler.Compile("Markdig", "namespace Markdig { public sealed class Other { } }") } };

        var report = (await AnalyseAsync(package)).Report;

        Assert.Equal(AnalysisOutcome.Failed, report.Verdict);
        Assert.Contains(report.Problems, problem => problem.Message == "lib/Markdig.dll is not a file of Markdig 0.40.0.");
    }

    [Fact]
    public async Task FailsAPackageFromTheFeedWithoutTheLockedHash()
    {
        var package = WithBundledPackages;
        var otherBytes = new PackageCache((package with { Locked = [("Markdig", "0.40.0", "MIT"), ("Sample.Native", "1.0.0", "Apache-2.0")] }).NuGetPackages());

        var report = (await AnalyseAsync(package, package.WriteSource(_folder.Combine("source")), otherBytes)).Report;

        Assert.Contains(report.Problems, problem => problem.Message == "Markdig 0.40.0 from the feed does not have the content hash its lock file records.");
        Assert.Contains(report.Problems, problem => problem.Message == "lib/native/linux-x64/libsample.so is not runtimes/linux-x64/native/libsample.so of any package the lock files pin.");
    }

    private static TestPackage WithBuildHost => new()
    {
        Capabilities = [("process", "Runs its build host.")],
        Locked = [("Sample.Host", "1.0.0", "MIT")],
        Content =
        [
            ("BuildHost/Host.exe", Compiler.Compile("Host", "namespace Host { public static class Program { public static void Main() => System.Diagnostics.Process.Start(\"dotnet\"); } }"), "Sample.Host"),
            ("BuildHost/Host.runtimeconfig.json", "{}"u8.ToArray(), "Sample.Host"),
        ],
    };

    [Fact]
    public async Task VerifiesContentFilesInSubfoldersAndScansTheirAssemblies()
    {
        var report = (await AnalyseAsync(WithBuildHost)).Report;

        Assert.DoesNotContain(report.Problems, problem => problem.Severity == ProblemSeverity.Error);
        Assert.Contains(new ReportedPackage("Sample.Host", "1.0.0", true, "MIT"), report.Dependencies!.Packages);
        var process = Assert.Single(report.Capabilities!.Detected, capability => capability.Id == "process");
        Assert.Contains(process.CallSites, site => site.Assembly == "BuildHost/Host.exe");
    }

    [Fact]
    public async Task FailsAContentFileThatIsNotTheFileOfItsPackage()
    {
        var package = WithBuildHost with { PublishedContent = new Dictionary<string, byte[]> { ["BuildHost/Host.runtimeconfig.json"] = "{ }"u8.ToArray() } };

        var report = (await AnalyseAsync(package)).Report;

        Assert.Equal(AnalysisOutcome.Failed, report.Verdict);
        Assert.Contains(report.Problems, problem => problem.Message == "lib/BuildHost/Host.runtimeconfig.json is not a file of Sample.Host 1.0.0.");
    }

    [Fact]
    public async Task FailsAnSbomThatDoesNotDescribeTheVerifiedContentsAndChecksTheVerifiedPackagesInstead()
    {
        _vulnerabilities.Advisories["pkg:nuget/Newtonsoft.Json@12.0.1"] = ["GHSA-5crp-9r3c-p9vr"];
        var package = new TestPackage { Locked = [("Newtonsoft.Json", "12.0.1", "MIT")], Sbom = Sbom(new TestPackage().Write(_folder.Combine("clean.rsplugin"))) };

        var report = (await AnalyseAsync(package)).Report;

        Assert.Contains(report.Problems, problem => problem.Code == ProblemCodes.BundledProvenance && problem.Location == CycloneDxSbom.FileName);
        Assert.Contains(report.Problems, problem => problem.Code == ProblemCodes.Vulnerability);
    }

    [Fact]
    public async Task FailsAPackageBuiltFromAnotherCommit()
    {
        var package = new TestPackage();
        var source = package.WriteSource(_folder.Combine("source")) with { Commit = new string('b', 40) };

        var report = (await AnalyseAsync(package, source, package.PackageSource())).Report;

        Assert.Contains(report.Problems, problem => problem.Code == ProblemCodes.Source && problem.Message.Contains("not the release commit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadsNoLockFileThroughASymbolicLink()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links needs extra rights on Windows.");
        var package = new TestPackage();
        var source = package.WriteSource(_folder.Combine("source"));
        var lockFile = Path.Combine(source.Repository, "a", LockFile.FileName);
        File.Move(lockFile, _folder.Combine("elsewhere.json"));
        File.CreateSymbolicLink(lockFile, _folder.Combine("elsewhere.json"));

        var report = (await AnalyseAsync(package, source, package.PackageSource())).Report;

        var problem = Assert.Single(report.Problems);
        Assert.Equal(ProblemCodes.Source, problem.Code);
        Assert.Contains("a/packages.lock.json is a symbolic link", problem.Message, StringComparison.Ordinal);
    }

    private static byte[] Sbom(string package)
    {
        using var snapshot = PackageSnapshot.Open(package, PackageLimits.Default);
        return snapshot.ReadFile(CycloneDxSbom.FileName);
    }

    [Fact]
    public async Task ReviewsLargeGrowth()
    {
        var big = new TestPackage { Version = "1.0.1", ExtraFiles = [new PackageFile("lib/data.txt", System.Security.Cryptography.RandomNumberGenerator.GetBytes(400_000))] };

        var report = (await AnalyseAsync(big, new TestPackage())).Report;

        Assert.Equal(["size-growth"], report.Triggers);
    }

    [Fact]
    public async Task ReportsAPackageThatBreaksThePackagingRules()
    {
        var path = _folder.Combine("broken.rsplugin");
        await File.WriteAllTextAsync(path, "not a zip", TestContext.Current.CancellationToken);

        var report = (await new PluginAnalyzer(_vulnerabilities, new NoPackages(), TimeProvider.System).AnalyseAsync(
            new AnalysisRequest(path, null, TestRegistrations.For("lumen.todo", TestPackage.Repository), new TestPackage().WriteSource(_folder.Combine("source")), null), TestContext.Current.CancellationToken)).Report;

        Assert.Equal(AnalysisOutcome.Failed, report.Verdict);
        Assert.Equal(ProblemCodes.PackageRules, Assert.Single(report.Problems).Code);
        Assert.Null(report.Manifest);
    }

    [Fact]
    public async Task FailsAManifestThatDoesNotMatchTheRegistration()
    {
        var report = (await AnalyseAsync(new TestPackage { Id = "lumen.other" })).Report;

        Assert.Contains(report.Problems, problem => problem.Code == ProblemCodes.Manifest && problem.Message.Contains("the ID in the registration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepsHeuristicDetailsOutOfThePublicReport()
    {
        var rules = new HeuristicRuleSet([new HeuristicRule { Id = "secret-rule-name", Kind = HeuristicKind.String, Pattern = "lumen-marker" }]);
        var package = new TestPackage { ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { public string Marker => \"lumen-marker\"; } }" };

        var result = await AnalyseAsync(package with { Version = "1.0.1" }, new TestPackage(), rules: rules);

        Assert.Equal(["heuristic"], result.Report.Triggers);
        Assert.Equal(HeuristicsState.Ran, result.Report.Heuristics);
        Assert.Equal("secret-rule-name", Assert.Single(result.HeuristicMatches!).Rule);
        var published = Json.ArtifactJson.SerializeToString(result.Report) + SummaryWriter.Write(result.Report);
        Assert.DoesNotContain("secret-rule-name", published, StringComparison.Ordinal);
        Assert.DoesNotContain("lumen-marker\"", published.Replace("\"lumen-marker\";", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("A review rule matched", published, StringComparison.Ordinal);
    }

    private static readonly PackageFile WebPage = new("web/index.html", "<!doctype html><script src=\"app.js\"></script>"u8.ToArray());

    private static readonly PackageFile WebScript = new("web/app.js", "runesmith.postMessage({ kind: 'ready' });"u8.ToArray());

    [Fact]
    public async Task ReviewsWebCodeInAFirstVersion()
    {
        var report = (await AnalyseAsync(new TestPackage { ExtraFiles = [WebPage, WebScript, new("web/engine.wasm", new byte[] { 0, 97, 115, 109 })] })).Report;

        Assert.Equal(AnalysisOutcome.Review, report.Verdict);
        Assert.Equal(["first-version", "web-content"], report.Triggers);
        Assert.Equal(3, report.Web!.Files);
        Assert.Equal(["web/app.js", "web/engine.wasm"], report.Web.Code);
        Assert.Null(report.Web.Changed);
        var problem = Assert.Single(report.Problems, problem => problem.Code == ProblemCodes.WebContent);
        Assert.Equal(ProblemSeverity.Review, problem.Severity);
        Assert.Equal("The web folder holds code web views run, in a first version: web/app.js, web/engine.wasm.", problem.Message);
    }

    [Fact]
    public async Task ReviewsWebCodeWhenAnyWebFileChanges()
    {
        var previous = new TestPackage { ExtraFiles = [WebPage, WebScript] };

        var unchanged = (await AnalyseAsync(previous with { Version = "1.0.1" }, previous)).Report;
        var page = (await AnalyseAsync(previous with { Version = "1.0.1", ExtraFiles = [new("web/index.html", "<!doctype html><p>Hi</p>"u8.ToArray()), WebScript] }, previous)).Report;
        var added = (await AnalyseAsync(previous with { Version = "1.0.1" }, new TestPackage())).Report;

        Assert.Empty(unchanged.Triggers);
        Assert.Empty(unchanged.Web!.Changed!);
        Assert.DoesNotContain(unchanged.Problems, problem => problem.Code == ProblemCodes.WebContent);
        Assert.Equal(["web-content"], page.Triggers);
        Assert.Equal(["web/index.html"], page.Web!.Changed);
        Assert.Contains(page.Problems, problem => problem.Message == "The web folder holds code web views run, and 1 web file changed since 1.0.0: web/app.js.");
        Assert.Equal(["web-content"], added.Triggers);
        Assert.Equal(["web/app.js", "web/index.html"], added.Web!.Changed);
    }

    [Fact]
    public async Task DoesNotReviewWebPagesWithoutCode()
    {
        var current = new TestPackage { Version = "1.0.1", ExtraFiles = [new("web/index.html", "<!doctype html>"u8.ToArray()), new("web/style.css", "p { }"u8.ToArray())] };

        var report = (await AnalyseAsync(current, new TestPackage())).Report;
        var removed = (await AnalyseAsync(new TestPackage { Version = "1.0.1" }, new TestPackage { ExtraFiles = [WebPage, WebScript] })).Report;

        Assert.Empty(report.Triggers);
        Assert.Equal(2, report.Web!.Files);
        Assert.Equal(["web/index.html", "web/style.css"], report.Web.Changed);
        Assert.Empty(report.Web.Code);
        Assert.Empty(removed.Triggers);
        Assert.Equal(["web/app.js", "web/index.html"], removed.Web!.Changed);
        Assert.Null((await AnalyseAsync(new TestPackage { Version = "1.0.1" }, new TestPackage())).Report.Web);
    }

    [Fact]
    public async Task ReviewsNetworkHostsThePreviousVersionDidNotDeclare()
    {
        var previous = new TestPackage { Capabilities = [("network", "Fetches todos.")], NetworkHosts = ["api.github.com"] };

        var same = (await AnalyseAsync(previous with { Version = "1.0.1", NetworkHosts = ["API.github.com"] }, previous)).Report;
        var added = (await AnalyseAsync(previous with { Version = "1.0.1", NetworkHosts = ["api.github.com", "*"] }, previous)).Report;
        var fromAny = (await AnalyseAsync(previous with { Version = "1.0.1", NetworkHosts = ["codeberg.org"] }, previous with { NetworkHosts = ["*"] })).Report;

        Assert.Empty(same.Triggers);
        Assert.Empty(same.Capabilities!.NetworkHostsAdded);
        Assert.Equal(["network-host-added"], added.Triggers);
        Assert.Equal(["*"], added.Capabilities!.NetworkHostsAdded);
        Assert.Contains("- It declares a network host the previous version did not.\n", SummaryWriter.Write(added), StringComparison.Ordinal);
        Assert.Empty(fromAny.Triggers);
    }

    [Fact]
    public void FailsAPackagedManifestWithNetworkHostsButNoNetworkCapability()
    {
        using var package = PackageSnapshot.Open(Write(new TestPackage()), PackageLimits.Default);
        var manifest = package.Manifest with { NetworkHosts = ["api.github.com"] };

        var problem = Assert.Single(PackagedManifestCheck.Check(manifest, TestRegistrations.For("lumen.todo", TestPackage.Repository)));

        Assert.Equal(ProblemCodes.Manifest, problem.Code);
        Assert.Equal(ProblemSeverity.Error, problem.Severity);
        Assert.Contains("networkHosts: is only allowed with the network capability", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsAnAnalysisWrittenBeforeWebContentAndNetworkHosts()
    {
        var report = (await AnalyseAsync(new TestPackage())).Report;
        var json = System.Text.RegularExpressions.Regex.Replace(Json.ArtifactJson.SerializeToString(report), ",\\s*\"networkHostsAdded\": \\[\\]", "");

        var read = AnalysisReport.Read(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.DoesNotContain("networkHostsAdded", json, StringComparison.Ordinal);
        Assert.Empty(read.Capabilities!.NetworkHostsAdded);
        Assert.Null(read.Web);
    }
}
