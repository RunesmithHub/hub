using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.EndToEnd;

public sealed class EndToEndTests(BuiltSamplePlugin sample) : IClassFixture<BuiltSamplePlugin>
{
    private const string Repository = "https://github.com/lumen/sample";

    private static PackageAssembler Assembler() => new(new BundleVerifier(new NoPackages()), SharedFrameworks.DefaultRoots(Environment.GetEnvironmentVariable));

    private Task<PackageResult> PackageAsync(string root, string output, bool archiveSource = true) =>
        Assembler().AssembleAsync(new PackageRequest(root, sample.Folder(output), sample.Commit, "https://github.com/RunesmithHub/registry/actions/runs/1", "10.0.400", archiveSource ? sample.SourceArchive : null), TestContext.Current.CancellationToken);

    private Task<AnalysisResult> AnalyseAsync(string package, string source) =>
        new PluginAnalyzer(new FakeVulnerabilities(), new NoPackages(), TimeProvider.System).AnalyseAsync(
            new AnalysisRequest(package, null, TestRegistrations.For(SamplePlugin.Id, Repository), new AnalysisSource(source, sample.Commit), null), TestContext.Current.CancellationToken);

    private void SkipUnlessBuilt() => Assert.SkipUnless(sample.Available, "dotnet and git must be on the PATH to build the sample plugin.");

    [Fact]
    public async Task PackagesTheBuildIntoAPackageThatFollowsEveryRule()
    {
        SkipUnlessBuilt();

        var result = await PackageAsync(sample.Repository, "package");

        await using var stream = File.OpenRead(result.PackageFile);
        using var archive = PackageArchive.Open(stream);
        var manifest = archive.ReadManifest();
        Assert.Equal("lumen.sample-1.0.0.rsplugin", Path.GetFileName(result.PackageFile));
        Assert.Equal("Lumen.Sample.Contracts.dll", manifest.Assemblies.Contracts.File);
        Assert.Equal("Lumen.Sample.dll", manifest.Assemblies.Implementation.File);
        Assert.Empty(manifest.Assemblies.Bundled);
        Assert.Equal(sample.Commit, manifest.Build.Commit);
        Assert.Equal(">=0.1.0 <0.2.0", manifest.RunesmithApi.ToString());
        Assert.Contains("lib/Lumen.Sample.Contracts.xml", archive.EntryNames);
        Assert.Contains("icon/64.png", archive.EntryNames);
        Assert.Contains("icon/128.png", archive.EntryNames);
        Assert.Contains("sbom.cdx.json", archive.EntryNames);
        Assert.Contains("THIRD-PARTY-NOTICES.md", archive.EntryNames);
        Assert.DoesNotContain(archive.EntryNames, entry => entry.Contains("Runesmith.Sdk", StringComparison.Ordinal));
        var contracts = await File.ReadAllBytesAsync(Path.Combine(sample.Folder("package"), "RunesmithHub.Lumen.Sample.Contracts.1.0.0.nupkg"), TestContext.Current.CancellationToken);
        Assert.Empty(ContractsNuGetPackage.Check(contracts, manifest, PackageLimits.Default.MaxUnpackedBytes));
        Assert.Equal(
            ["RunesmithHub.Lumen.Sample.Contracts.1.0.0.nupkg", "hashes.json", "images/icon-128.png", "images/icon-256.png", "images/icon-32.png", "images/icon-64.png", "images/images.json",
                "lumen.sample-1.0.0.cdx.json", "lumen.sample-1.0.0.rsplugin", "lumen.sample-1.0.0.source.tar.gz"],
            Directory.EnumerateFiles(sample.Folder("package"), "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(sample.Folder("package"), file).Replace('\\', '/')).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TwoPackagingRunsGiveIdenticalHashes()
    {
        SkipUnlessBuilt();

        var first = await PackageAsync(sample.Repository, "first");
        var second = await PackageAsync(sample.Repository, "second");

        Assert.Empty(first.Hashes.Differences(second.Hashes));
        Assert.Equal(first.Hashes, HashList.Read(Path.Combine(sample.Folder("second"), HashList.FileName)), new HashListComparer());
    }

    [Fact]
    public async Task FailsAPlantedNetworkUseThatIsNotDeclared()
    {
        SkipUnlessBuilt();
        var package = await PackageAsync(sample.Repository, "undeclared");

        var report = (await AnalyseAsync(package.PackageFile, sample.Repository)).Report;

        Assert.Equal(AnalysisOutcome.Failed, report.Verdict);
        Assert.Equal(["network", "credentials"], report.Capabilities!.Detected.Select(capability => capability.Id));
        var undeclared = Assert.Single(report.Problems, problem => problem.Code == ProblemCodes.CapabilityUndeclared);
        Assert.Contains("network", undeclared.Message, StringComparison.Ordinal);
        Assert.Contains("Lumen.Sample.Greeter.FetchAsync", undeclared.Message, StringComparison.Ordinal);
        Assert.Contains(report.Problems, problem => problem.Code == ProblemCodes.CapabilityUnused && problem.Message.Contains("filesystem", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Problems, problem => problem.Severity == ProblemSeverity.Error && problem.Code != ProblemCodes.CapabilityUndeclared);
    }

    [Fact]
    public async Task PassesWithReviewOnceTheCapabilityIsDeclared()
    {
        SkipUnlessBuilt();
        var copy = sample.Folder("declared");
        CopyWithoutGit(sample.Repository, copy);
        SamplePlugin.WritePlugin(Path.Combine(sample.Folder("declared-source")), Path.Combine(sample.Folder("sdk"), "out", "Runesmith.Sdk.dll"), declareNetwork: true);
        File.Copy(Path.Combine(sample.Folder("declared-source"), "plugin.json"), Path.Combine(copy, "plugin.json"), overwrite: true);
        var package = await PackageAsync(copy, "declared-package", archiveSource: false);

        var result = await AnalyseAsync(package.PackageFile, copy);

        Assert.Equal(AnalysisOutcome.Review, result.Report.Verdict);
        Assert.Equal(["first-version"], result.Report.Triggers);
        Assert.Equal("RunesmithHub.Lumen.Sample.Contracts", result.Report.Contracts!.Package);
        Assert.Null(result.HeuristicMatches);
    }

    [Fact]
    public async Task PacksTheWebFolderAndReviewsItsCode()
    {
        SkipUnlessBuilt();
        var copy = sample.Folder("web");
        CopyWithoutGit(sample.Repository, copy);
        Directory.CreateDirectory(Path.Combine(copy, "web", "assets"));
        File.WriteAllText(Path.Combine(copy, "web", "index.html"), "<!doctype html>");
        File.WriteAllText(Path.Combine(copy, "web", "assets", "app.js"), "run();");

        var package = await PackageAsync(copy, "web-package", archiveSource: false);

        await using (var stream = File.OpenRead(package.PackageFile))
        {
            using var archive = PackageArchive.Open(stream);
            Assert.Equal(["web/assets/app.js", "web/index.html"], archive.EntryNames.Where(PackageEntryRules.IsWebFile));
        }

        var report = (await AnalyseAsync(package.PackageFile, copy)).Report;
        Assert.Contains("web-content", report.Triggers);
        Assert.Equal(["web/assets/app.js"], report.Web!.Code);
    }

    [Fact]
    public async Task RefusesAWebFolderWithOtherFileTypes()
    {
        SkipUnlessBuilt();
        var copy = sample.Folder("web-type");
        CopyWithoutGit(sample.Repository, copy);
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(copy, "web")).FullName, "run.sh"), "echo hi");

        var exception = await Assert.ThrowsAsync<PackageException>(() => PackageAsync(copy, "web-type-package", archiveSource: false));

        Assert.Contains("\"web/run.sh\" has a file type web pages may not contain.", exception.Problems);
    }

    [Fact]
    public async Task ReadsNoWebFileThroughASymbolicLink()
    {
        SkipUnlessBuilt();
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links needs extra rights on Windows.");
        var copy = sample.Folder("web-link");
        CopyWithoutGit(sample.Repository, copy);
        var web = Directory.CreateDirectory(Path.Combine(copy, "web", "pages")).FullName;
        File.CreateSymbolicLink(Path.Combine(web, "notes.txt"), Path.Combine(copy, "LICENSE"));

        var exception = await Assert.ThrowsAsync<ToolException>(() => PackageAsync(copy, "web-link-package", archiveSource: false));

        Assert.Contains("web/pages/notes.txt is a symbolic link", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsNoBuildOutputThroughASymbolicLink()
    {
        SkipUnlessBuilt();
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links needs extra rights on Windows.");
        var copy = sample.Folder("output-link");
        CopyWithoutGit(sample.Repository, copy);
        var symbols = Path.Combine(copy, "Lumen.Sample", "bin", "Release", "net10.0", "Lumen.Sample.pdb");
        File.Delete(symbols);
        File.CreateSymbolicLink(symbols, Path.Combine(sample.Folder("sdk"), "out", "Runesmith.Sdk.dll"));

        var exception = await Assert.ThrowsAsync<ToolException>(() => PackageAsync(copy, "output-link-package", archiveSource: false));

        Assert.Contains("Lumen.Sample/bin/Release/net10.0/Lumen.Sample.pdb is a symbolic link", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsNoRestoreOutputThroughALinkedFolder()
    {
        SkipUnlessBuilt();
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links needs extra rights on Windows.");
        var copy = sample.Folder("obj-link");
        CopyWithoutGit(sample.Repository, copy);
        var obj = Path.Combine(copy, "Lumen.Sample", "obj");
        Directory.Move(obj, Path.Combine(sample.Folder("obj-link-target")));
        Directory.CreateSymbolicLink(obj, sample.Folder("obj-link-target"));

        var exception = await Assert.ThrowsAsync<ToolException>(() => PackageAsync(copy, "obj-link-package", archiveSource: false));

        Assert.Contains("Lumen.Sample/obj is a symbolic link", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePackageCommandWritesTheOutputsOfTheBuildJob()
    {
        SkipUnlessBuilt();

        var (exitCode, output, error) = await new ToolRunner().RunAsync(
            "build", "package", "--source", sample.Repository, "--output", sample.Folder("command"), "--commit", sample.Commit, "--source-archive", sample.SourceArchive, "--run", "https://github.com/RunesmithHub/registry/actions/runs/1");

        Assert.True(exitCode == 0, error);
        Assert.Contains("lumen.sample-1.0.0.rsplugin; hashes.json lists all 9 output files.", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(sample.Folder("command"), HashList.FileName)));
    }

    [Fact]
    public async Task TheCheckCommandReportsTheFailureOfAnExistingBuild()
    {
        SkipUnlessBuilt();

        var (exitCode, output, error) = await new ToolRunner().RunAsync("check", sample.Repository, "--no-build", "--output", sample.Folder("check"));

        Assert.Equal(1, exitCode);
        Assert.Empty(error);
        Assert.Contains("**Verdict: failed**", output, StringComparison.Ordinal);
        Assert.Contains("without declaring it", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(sample.Folder("check"), "analysis", AnalysisReport.FileName)));
    }

    [Fact]
    public async Task TheCheckCommandBuildsALocalizedPluginAndShowsItsLanguages()
    {
        SkipUnlessBuilt();
        var copy = sample.Folder("localized");
        CopyWithoutGit(sample.Repository, copy);
        SamplePlugin.WritePlugin(sample.Folder("localized-source"), Path.Combine(sample.Folder("sdk"), "out", "Runesmith.Sdk.dll"), declareNetwork: true);
        var manifest = File.ReadAllText(Path.Combine(sample.Folder("localized-source"), "plugin.json"));
        File.WriteAllText(Path.Combine(copy, "plugin.json"), manifest.Replace("\"icon\": \"icon.png\",", "\"languages\": [\"en\", \"nl\"],\n  \"icon\": \"icon.png\",", StringComparison.Ordinal));
        var project = Path.Combine(copy, "Lumen.Sample");
        foreach (var (language, content) in new[] { ("en", "greeting = Hello, { $name }\nfarewell = Goodbye\n"), ("nl", "greeting = Hallo, { $name }\n") })
        {
            Directory.CreateDirectory(Path.Combine(project, "locales", language));
            File.WriteAllText(Path.Combine(project, "locales", language, "lumen.sample.ftl"), content);
        }

        var csproj = Path.Combine(project, "Lumen.Sample.csproj");
        File.WriteAllText(csproj, File.ReadAllText(csproj).Replace("</Project>", """
              <ItemGroup>
                <EmbeddedResource Include="locales/en/lumen.sample.ftl" LogicalName="locales/en/lumen.sample.ftl" />
                <EmbeddedResource Include="locales/nl/lumen.sample.ftl" LogicalName="locales/nl/lumen.sample.ftl" />
              </ItemGroup>
            </Project>
            """, StringComparison.Ordinal));

        var (exitCode, output, error) = await new ToolRunner().RunAsync("check", copy, "--output", sample.Folder("localized-check"));

        Assert.True(exitCode == 0, output + error);
        Assert.Contains("**Verdict: passed, needs review**", output, StringComparison.Ordinal);
        Assert.Contains("\n## Languages\n\n| Language | Messages |\n| --- | --- |\n| en | 2 |\n| nl | 1 |\n", output.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    private static void CopyWithoutGit(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith(".git", StringComparison.Ordinal))
                continue;
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private sealed class HashListComparer : IEqualityComparer<HashList>
    {
        public bool Equals(HashList? x, HashList? y) => x is not null && y is not null && x.Files.SequenceEqual(y.Files);

        public int GetHashCode(HashList obj) => obj.Files.Count;
    }
}
