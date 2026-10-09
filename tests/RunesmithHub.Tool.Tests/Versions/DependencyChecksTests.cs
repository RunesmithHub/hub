using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Registry;
using RunesmithHub.Tool.Tests.Support;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Tests.Versions;

public sealed class DependencyChecksTests
{
    private static readonly HostedImage Image = new() { Url = "https://example.com/i.png", Sha256 = new string('0', 64), Length = 1, Width = 64, Height = 64, Format = ImageFormat.Png };

    private static PluginDependency On(string id, string range) => new() { Id = id, Range = VersionRange.Parse(range).Normalize() };

    internal static RegistryVersionRecord Version(string version, VersionState state = VersionState.Published, ReviewState review = ReviewState.Automated, string? contracts = null,
        params PluginDependency[] dependencies) => new()
        {
            Version = SemanticVersion.Parse(version),
            State = state,
            Review = new VersionReview { State = review },
            Commit = new string('a', 40),
            Tag = "v" + version,
            ReleaseAuthor = Samples.Maintainer,
            Contracts = new ContractsPackage { Assembly = contracts ?? "Other.Contracts.dll", Sha256 = new string('0', 64), Package = "RunesmithHub.Other.Contracts", PackageVersion = version },
            RunesmithApi = VersionRange.Parse("^0.1.0").Normalize(),
            Dependencies = dependencies,
            Capabilities = new CapabilityReport { Declared = [], Detected = [] },
            NetworkHosts = [],
            Platforms = [],
            Analysis = new AnalysisSummary { Verdict = AnalysisVerdict.Passed, Triggers = [] },
            Build = new BuildSummary { Run = "local", Sdk = "10.0.400", Reproducible = true },
            ReleaseNotes = "",
            PublishedAt = state == VersionState.Published ? Samples.Time : null,
            Summary = "A plugin.",
            Name = "A plugin",
            Categories = ["Other"],
            Keywords = [],
            TrackingIssue = 1,
            BuildRun = 1,
            AnalysisDigest = new string('0', 64),
        };

    internal static PluginRecord Plugin(string id, PluginTier tier, params VersionRecord[] versions) => new()
    {
        Type = PayloadTypes.Plugin,
        Version = 1,
        Id = id,
        Name = id,
        Publisher = id.Split('.')[0],
        Tier = tier,
        State = PluginState.Listed,
        Repository = new RecordRepository { Id = 1, Url = "https://github.com/x/y", Path = "." },
        Maintainers = [],
        Summary = "A plugin.",
        Categories = ["Other"],
        Keywords = [],
        Icon = new IconSet { Size64 = Image, Size128 = Image, Size256 = Image },
        Gallery = [],
        Versions = versions,
    };

    private static HubCatalog Catalog(params PluginRecord[] plugins) =>
        new(plugins, new PublisherList { Type = PayloadTypes.Publishers, Version = 1, Publishers = [] }, []);

    private static PackagedManifest Manifest(params PluginDependency[] dependencies) => new()
    {
        SchemaVersion = 1,
        Id = Samples.PluginId,
        Name = "Lumen Todo",
        Version = SemanticVersion.Parse("1.0.0"),
        Summary = "Collects TODO comments.",
        Authors = ["Lumen Labs"],
        License = "MIT",
        Repository = "https://github.com/lumen-labs/lumen-todo",
        RunesmithApi = VersionRange.Parse("^0.1.0").Normalize(),
        Dependencies = dependencies,
        Categories = ["Other"],
        Assemblies = new PackagedAssemblies
        {
            Contracts = new AssemblyFile { File = "Lumen.Todo.Contracts.dll", Sha256 = new string('0', 64) },
            Implementation = new AssemblyFile { File = "Lumen.Todo.dll", Sha256 = new string('1', 64) },
        },
        Build = new PackageBuild { Commit = new string('a', 40), Run = "local", Sdk = "10.0.400" },
    };

    private static RegistryState Registry(params (string Id, RegistryVersionRecord Record)[] versions) => new(
        [],
        [Samples.Registration(), .. versions.Select(version => version.Id).Distinct().Select(id => Samples.Registration(id))],
        versions.GroupBy(version => version.Id).ToDictionary(group => group.Key, group => (IReadOnlyList<RegistryVersionRecord>)[.. group.Select(version => version.Record)]),
        [],
        new RegistryPolicy(),
        []);

    private static IReadOnlyList<string> Check(PackagedManifest manifest, HubCatalog catalog, PluginTier tier = PluginTier.Unverified, RegistryState? registry = null) =>
        DependencyChecks.Check(manifest, Samples.Registration(tier: tier), registry ?? Registry(), catalog);

    [Fact]
    public void PassesADependencyWithAPublishedVersionInRange()
    {
        var catalog = Catalog(Plugin("harbor.issues", PluginTier.Unverified, Version("1.2.0"), Version("2.0.0")));

        Assert.Empty(Check(Manifest(On("harbor.issues", "^1.0.0")), catalog));
    }

    [Fact]
    public void FailsADependencyThatIsNotOnTheHubOrHasNoPublishedVersionInRange()
    {
        var catalog = Catalog(Plugin("harbor.issues", PluginTier.Unverified, Version("1.2.0", VersionState.Yanked), Version("2.0.0")));

        var problems = Check(Manifest(On("harbor.issues", "^1.0.0"), On("ghost.plugin", "^1.0.0")), catalog);

        Assert.Equal(
            ["No published version of harbor.issues (harbor.issues) satisfies >=1.0.0 <2.0.0.", "The dependency ghost.plugin is not a plugin on the hub."],
            problems);
    }

    [Theory]
    [InlineData(PluginTier.Official, PluginTier.Verified, false)]
    [InlineData(PluginTier.Official, PluginTier.Official, true)]
    [InlineData(PluginTier.Verified, PluginTier.Official, true)]
    [InlineData(PluginTier.Verified, PluginTier.Unverified, false)]
    [InlineData(PluginTier.Unverified, PluginTier.Unverified, true)]
    public void AppliesTheTrustRulesOfTheTiers(PluginTier dependent, PluginTier dependency, bool allowed)
    {
        var catalog = Catalog(Plugin("harbor.issues", dependency, Version("1.0.0", review: ReviewState.Reviewed)));

        var problems = Check(Manifest(On("harbor.issues", "^1.0.0")), catalog, dependent);

        Assert.Equal(allowed, problems.Count == 0);
    }

    [Fact]
    public void LetsVerifiedPluginsResolveOnlyToReviewedVersions()
    {
        var catalog = Catalog(Plugin("harbor.issues", PluginTier.Verified, Version("1.0.0")));

        var problem = Assert.Single(Check(Manifest(On("harbor.issues", "^1.0.0")), catalog, PluginTier.Verified));

        Assert.Contains("No reviewed, published version", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void FailsAVersionThatWouldCloseACycleThroughAPublishedVersionInRange()
    {
        var catalog = Catalog(
            Plugin("harbor.issues", PluginTier.Unverified, Version("1.0.0", dependencies: On("harbor.tracker", "^1.0.0"))),
            Plugin("harbor.tracker", PluginTier.Unverified, Version("1.0.0", dependencies: On(Samples.PluginId, "^1.0.0"))));

        var problem = Assert.Single(Check(Manifest(On("harbor.issues", "^1.0.0")), catalog));

        Assert.Equal("The dependencies close a cycle: lumen.todo -> harbor.issues -> harbor.tracker -> lumen.todo.", problem);
    }

    [Fact]
    public void IgnoresADependencyBackOnVersionsOutsideItsRange()
    {
        var catalog = Catalog(Plugin("harbor.issues", PluginTier.Unverified, Version("1.0.0", dependencies: On(Samples.PluginId, "^0.5.0"))));

        Assert.Empty(Check(Manifest(On("harbor.issues", "^1.0.0")), catalog));
    }

    [Fact]
    public void FailsAClosureOfMoreThan32Plugins()
    {
        var plugins = Enumerable.Range(0, 33).Select(i => Plugin($"chain.p{i:D2}", PluginTier.Unverified,
            Version("1.0.0", dependencies: i < 32 ? [On($"chain.p{i + 1:D2}", "^1.0.0")] : []))).ToArray();

        var problem = Assert.Single(Check(Manifest(On("chain.p00", "^1.0.0")), Catalog(plugins)));

        Assert.Contains("33 plugins", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void FailsAnAssemblyNamedLikeAnotherPluginsContracts()
    {
        var registry = Registry(("harbor.issues", Version("1.0.0", contracts: "Lumen.Todo.dll")));

        var problem = Assert.Single(Check(Manifest(), Catalog(), registry: registry));

        Assert.Equal("Lumen.Todo.dll has the name of the contracts assembly of harbor.issues; rename the assembly.", problem);
    }
}
