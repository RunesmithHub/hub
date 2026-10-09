using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Resolution;
using RunesmithHub.Protocol.Tests.Support;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Tests.Resolution;

public sealed class ResolverTests
{
    private static readonly RunesmithHost Host = new(SemanticVersion.Parse("0.1.0"), SemanticVersion.Parse("0.1.0"), Platforms.Linux);

    private readonly List<PluginRecord> _plugins = [];
    private readonly List<ModerationEntry> _moderation = [];
    private readonly List<InstalledPlugin> _installed = [];

    private ResolutionSettings _settings = new(Host);

    private static VersionRecord V(string version, VersionState state = VersionState.Published, ReviewState review = ReviewState.Reviewed, string api = "^0.1.0", params (string, string)[] deps) =>
        Records.Version(version, state, review, api, deps);

    private void Add(string id, string name, PluginTier tier, params VersionRecord[] versions) => _plugins.Add(Records.Plugin(id, name, tier, versions));

    private void Installed(string id, string version, bool requested = true) => _installed.Add(new InstalledPlugin(id, SemanticVersion.Parse(version), requested));

    private void Block(string id, string range) => _moderation.Add(Records.Moderation(_moderation.Count + 1,
        new ModerationTarget { Kind = ModerationTargetKind.Versions, Plugin = id, Range = VersionRange.Parse(range) }, ModerationAction.Block));

    private ResolutionResult Resolve(ResolutionRequest request) => new Resolver(Records.Catalog(_plugins, _moderation), _settings).Resolve(_installed, request);

    private InstallPlan Plan(ResolutionRequest request)
    {
        var result = Resolve(request);
        Assert.True(result.Succeeded, result.Failure?.Message);
        return result.Plan;
    }

    private ResolutionFailure Failure(ResolutionRequest request)
    {
        var result = Resolve(request);
        Assert.False(result.Succeeded, "Expected no plan.");
        return result.Failure;
    }

    private static string Describe(InstallPlan plan) =>
        string.Join("; ", plan.Operations.Select(op => $"{op.Kind} {op.PluginId} {op.From?.ToString() ?? "-"}>{op.To?.ToString() ?? "-"} {op.Reason}"));

    /// <summary>Runesmith Git, Harbor Issues and Lumen Todo as in the worked example of the dependencies design.</summary>
    private void WorkedExample()
    {
        Add("runesmith.git", "Runesmith Git", PluginTier.Official, V("0.2.9"), V("0.3.0"), V("0.3.3"), V("0.3.4"));
        Add("harbor.issues", "Harbor Issues", PluginTier.Verified,
            V("1.2.0", deps: ("runesmith.git", "^0.3.0")),
            V("1.3.0", deps: ("runesmith.git", "^0.3.0")),
            V("1.3.1", deps: ("runesmith.git", "^0.3.0")));
        Add("lumen.todo", "Lumen Todo", PluginTier.Unverified,
            V("0.9.2", review: ReviewState.Automated, deps: [("harbor.issues", ">=1.2.0"), ("runesmith.git", "^0.3.1")]),
            V("1.0.0", review: ReviewState.Automated, deps: [("harbor.issues", ">=1.2.0"), ("runesmith.git", "^0.3.1")]),
            V("1.1.0", VersionState.Held, ReviewState.Automated, deps: [("harbor.issues", ">=1.2.0"), ("runesmith.git", "^0.3.1")]));
    }

    [Fact]
    public void ResolvesTheWorkedExample()
    {
        WorkedExample();
        Installed("runesmith.git", "0.3.0");

        var plan = Plan(ResolutionRequest.Install("lumen.todo"));

        Assert.Equal(
            "Update runesmith.git 0.3.0>0.3.4 Requested; Install harbor.issues ->1.3.1 Dependency; Install lumen.todo ->1.0.0 Requested",
            Describe(plan));
        Assert.Equal(PluginTier.Unverified, plan.Find("lumen.todo")!.Tier);
        Assert.Equal("Harbor Issues", plan.Find("harbor.issues")!.Name);
        Assert.Equal(SemanticVersion.Parse("1.3.1"), plan.Find("harbor.issues")!.Record!.Version);
    }

    [Fact]
    public void MovesOffABlockedVersionToTheFix()
    {
        WorkedExample();
        _plugins[1] = _plugins[1] with { Versions = [V("1.3.2", deps: ("runesmith.git", "^0.3.0")), .. _plugins[1].Versions] };
        Block("harbor.issues", "1.3.1");
        Installed("lumen.todo", "1.0.0");
        Installed("harbor.issues", "1.3.1", requested: false);
        Installed("runesmith.git", "0.3.4");

        var plan = Plan(ResolutionRequest.Sync());

        Assert.Equal(
            "Keep runesmith.git 0.3.4>0.3.4 Requested; Update harbor.issues 1.3.1>1.3.2 Dependency; Keep lumen.todo 1.0.0>1.0.0 Requested",
            Describe(plan));
        Assert.Equal([OperationKind.Update], plan.Changes.Select(op => op.Kind));
    }

    [Fact]
    public void ExplainsTwoPluginsNeedingDifferentLines()
    {
        WorkedExample();
        Add("harbor.blame", "Harbor Blame", PluginTier.Verified, V("2.0.0", deps: ("runesmith.git", "^0.2.0")));
        _plugins[2] = _plugins[2] with
        {
            Versions = [V("1.4.0", review: ReviewState.Automated, deps: ("runesmith.git", "^0.3.1")), V("1.3.0", review: ReviewState.Automated, deps: ("runesmith.git", "^0.2.0"))],
        };
        Installed("harbor.blame", "2.0.0");
        Installed("runesmith.git", "0.2.9", requested: false);

        var failure = Failure(ResolutionRequest.Install("lumen.todo", SemanticVersion.Parse("1.4.0")));

        Assert.Equal(ResolutionFailureKind.NoSolution, failure.Kind);
        Assert.Equal(
            "Lumen Todo 1.4.0 needs Runesmith Git 0.3.1 or later in 0.3, and Harbor Blame needs Runesmith Git 0.2. No version of Runesmith Git satisfies both. " +
            "Remove Harbor Blame, or install Lumen Todo 1.3.0, which works with Runesmith Git 0.2.",
            failure.Message);
        Assert.Contains(failure.Derivation, line => line.StartsWith("Because", StringComparison.Ordinal));
    }

    [Fact]
    public void PrefersAVersionThatKeepsWhatIsInstalled()
    {
        WorkedExample();
        Add("harbor.blame", "Harbor Blame", PluginTier.Verified, V("2.0.0", deps: ("runesmith.git", "^0.2.0")));
        _plugins[2] = _plugins[2] with
        {
            Versions = [V("1.4.0", review: ReviewState.Automated, deps: ("runesmith.git", "^0.3.1")), V("1.3.0", review: ReviewState.Automated, deps: ("runesmith.git", "^0.2.0"))],
        };
        Installed("harbor.blame", "2.0.0");
        Installed("runesmith.git", "0.2.9", requested: false);

        var plan = Plan(ResolutionRequest.Install("lumen.todo"));

        Assert.Equal([("lumen.todo", "1.3.0")], plan.Changes.Select(op => (op.PluginId, op.To!.ToString())));
    }

    [Fact]
    public void ExplainsADependencyWhoseMatchingVersionsAreAllBlocked()
    {
        Add("northwind.charts", "Northwind Charts", PluginTier.Verified, V("1.9.0"), V("1.9.4"), V("2.0.0"), V("2.1.0"));
        Add("lumen.todo", "Lumen Todo", PluginTier.Unverified, V("1.0.0", review: ReviewState.Automated, deps: ("northwind.charts", "^1.0.0")));
        Block("northwind.charts", ">=1.0.0 <2.0.0");

        var failure = Failure(ResolutionRequest.Install("lumen.todo"));

        Assert.Equal(
            "Lumen Todo needs Northwind Charts 1.x, and every 1.x version of Northwind Charts is blocked. Lumen Todo can't be installed until it moves to Northwind Charts 2.x.",
            failure.Message);
    }

    [Fact]
    public void ExplainsADependencyTheTierSettingExcludes()
    {
        Add("harbor.issues", "Harbor Issues", PluginTier.Unverified, V("1.3.0", review: ReviewState.Automated));
        Add("lumen.todo", "Lumen Todo", PluginTier.Verified, V("1.0.0", deps: ("harbor.issues", "^1.2.0")));
        _settings = _settings with { AllowedTiers = AllowedTiers.OfficialAndVerified };

        var failure = Failure(ResolutionRequest.Install("lumen.todo"));

        Assert.Equal("Lumen Todo needs Harbor Issues, which is unverified. Your settings allow only official and verified plugins.", failure.Message);
    }

    [Fact]
    public void ExplainsARequestedPluginTheTierSettingExcludes()
    {
        Add("lumen.todo", "Lumen Todo", PluginTier.Unverified, V("1.0.0", review: ReviewState.Automated));
        _settings = _settings with { AllowedTiers = AllowedTiers.OfficialOnly };

        Assert.Equal("Lumen Todo is unverified. Your settings allow only official plugins.", Failure(ResolutionRequest.Install("lumen.todo")).Message);
    }

    [Fact]
    public void ExplainsARunesmithThatIsTooOld()
    {
        Add("lumen.todo", "Lumen Todo", PluginTier.Unverified, V("1.0.0", api: "^0.5.0"), V("0.9.0", api: "^0.5.0"));
        _settings = new ResolutionSettings(new RunesmithHost(SemanticVersion.Parse("0.4.2"), SemanticVersion.Parse("0.4.0"), null));

        var failure = Failure(ResolutionRequest.Install("lumen.todo"));

        Assert.Equal(
            "No version of Lumen Todo runs on this version of Runesmith. The newest version needs Runesmith API 0.5; this Runesmith provides 0.4. Update Runesmith.",
            failure.Message);
    }

    [Fact]
    public void ExplainsARunesmithThatIsTooNew()
    {
        Add("lumen.todo", "Lumen Todo", PluginTier.Unverified, V("1.0.0", api: "^0.3.0"));
        _settings = new ResolutionSettings(new RunesmithHost(SemanticVersion.Parse("0.4.2"), SemanticVersion.Parse("0.4.0"), null));

        Assert.EndsWith("this Runesmith provides 0.4. Lumen Todo needs a new version for this Runesmith.", Failure(ResolutionRequest.Install("lumen.todo")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AppliesTheTrustRulesToDependencies()
    {
        Add("harbor.issues", "Harbor Issues", PluginTier.Verified, V("1.3.0"));
        Add("runesmith.github", "GitHub", PluginTier.Official, V("0.2.1", deps: ("harbor.issues", "^1.0.0")));

        Assert.Equal(
            "GitHub needs Harbor Issues, which is verified. Official plugins can only depend on official plugins.",
            Failure(ResolutionRequest.Install("runesmith.github")).Message);
    }

    [Fact]
    public void ResolvesVerifiedDependentsOnlyToReviewedVersions()
    {
        Add("runesmith.git", "Runesmith Git", PluginTier.Official, V("0.3.0"), V("0.3.5", review: ReviewState.Automated));
        Add("harbor.issues", "Harbor Issues", PluginTier.Verified, V("1.3.0", deps: ("runesmith.git", "^0.3.0")));
        Add("lumen.todo", "Lumen Todo", PluginTier.Unverified, V("1.0.0", review: ReviewState.Automated, deps: ("runesmith.git", ">=0.3.5")));

        Assert.Equal("0.3.0", Plan(ResolutionRequest.Install("harbor.issues")).Find("runesmith.git")!.To!.ToString());
        Assert.Equal("0.3.5", Plan(ResolutionRequest.Install("lumen.todo")).Find("runesmith.git")!.To!.ToString());

        Installed("lumen.todo", "1.0.0");
        Installed("runesmith.git", "0.3.5", requested: false);
        Assert.StartsWith("Harbor Issues needs a reviewed version of Runesmith Git", Failure(ResolutionRequest.Install("harbor.issues")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovesAPluginAndTheDependenciesNothingNeedsAnymore()
    {
        WorkedExample();
        Installed("lumen.todo", "1.0.0");
        Installed("harbor.issues", "1.3.1", requested: false);
        Installed("runesmith.git", "0.3.4");

        var plan = Plan(ResolutionRequest.Remove("lumen.todo"));

        Assert.Equal(
            "Remove harbor.issues 1.3.1>- NoLongerNeeded; Remove lumen.todo 1.0.0>- Requested; Keep runesmith.git 0.3.4>0.3.4 Requested",
            Describe(plan));
    }

    [Fact]
    public void ListsThePluginsThatNeedWhatIsBeingRemoved()
    {
        WorkedExample();
        Installed("lumen.todo", "1.0.0");
        Installed("harbor.issues", "1.3.1", requested: false);
        Installed("runesmith.git", "0.3.4");

        var failure = Failure(ResolutionRequest.Remove("runesmith.git"));
        var all = Plan(ResolutionRequest.Remove("runesmith.git", "lumen.todo", "harbor.issues"));

        Assert.Equal(ResolutionFailureKind.NeededByOthers, failure.Kind);
        Assert.Equal(["harbor.issues", "lumen.todo"], failure.Dependents.Order());
        Assert.Equal("Lumen Todo and Harbor Issues need Runesmith Git. Remove them too, or cancel.", failure.Message);
        Assert.All(all.Operations, op => Assert.Equal(OperationKind.Remove, op.Kind));
    }

    [Fact]
    public void KeepsAnInstalledFrozenYankedOrDeletedVersionButNeverMovesOntoOne()
    {
        Add("pixel.icons", "Pixel File Icons", PluginTier.Unverified, V("2.1.3", review: ReviewState.Automated), V("2.2.0", VersionState.Frozen, ReviewState.Automated));
        Add("copper.snippets", "Copper Snippets", PluginTier.Unverified, V("1.1.0", review: ReviewState.Automated), V("1.2.0", VersionState.Yanked, ReviewState.Automated));
        Add("atlas.rest", "Atlas REST Client", PluginTier.Unverified, V("0.6.0", VersionState.Deleted, ReviewState.Automated));
        Installed("pixel.icons", "2.2.0");
        Installed("copper.snippets", "1.2.0");
        Installed("atlas.rest", "0.6.0");

        Assert.Empty(Plan(ResolutionRequest.Sync()).Changes);
        Assert.Equal("2.2.0", Plan(ResolutionRequest.UpdateAll()).Find("pixel.icons")!.To!.ToString());

        _installed.Clear();
        Assert.Equal("2.1.3", Plan(ResolutionRequest.Install("pixel.icons")).Find("pixel.icons")!.To!.ToString());
        Assert.Equal("1.1.0", Plan(ResolutionRequest.Install("copper.snippets")).Find("copper.snippets")!.To!.ToString());
        Assert.Equal("Atlas REST Client 0.6.0 is deleted.", Failure(ResolutionRequest.Install("atlas.rest")).Message);
    }

    [Fact]
    public void InstallsAYankedVersionOnlyWhenAskedForExactly()
    {
        Add("copper.snippets", "Copper Snippets", PluginTier.Unverified, V("1.1.0", review: ReviewState.Automated), V("1.2.0", VersionState.Yanked, ReviewState.Automated));
        Add("pixel.icons", "Pixel File Icons", PluginTier.Unverified, V("2.2.0", VersionState.Frozen, ReviewState.Automated));

        Assert.Equal("1.2.0", Plan(ResolutionRequest.Install("copper.snippets", SemanticVersion.Parse("1.2.0"))).Find("copper.snippets")!.To!.ToString());
        Assert.Equal("Pixel File Icons 2.2.0 is frozen.", Failure(ResolutionRequest.Install("pixel.icons", SemanticVersion.Parse("2.2.0"))).Message);
    }

    [Fact]
    public void ChoosesPreReleasesOnlyWhenAllowed()
    {
        Add("runesmith.git", "Runesmith Git", PluginTier.Official, V("0.3.4"), V("0.4.0-beta.1"));
        Add("lumen.todo", "Lumen Todo", PluginTier.Unverified, V("1.0.0", review: ReviewState.Automated, deps: ("runesmith.git", ">=0.4.0-beta.1 <0.5.0")));

        Assert.Equal("0.3.4", Plan(ResolutionRequest.Install("runesmith.git")).Find("runesmith.git")!.To!.ToString());
        Assert.Equal("0.4.0-beta.1", Plan(ResolutionRequest.Install("lumen.todo")).Find("runesmith.git")!.To!.ToString());

        _settings = _settings with { PreReleaseOptIns = new HashSet<string> { "runesmith.git" } };
        Assert.Equal("0.4.0-beta.1", Plan(ResolutionRequest.Install("runesmith.git")).Find("runesmith.git")!.To!.ToString());
    }

    [Fact]
    public void SkipsVersionsForOtherPlatforms()
    {
        Add("cobalt.docker", "Cobalt Docker", PluginTier.Unverified, Records.Version("0.3.2", review: ReviewState.Automated, platforms: [Platforms.Linux]),
            Records.Version("0.4.0", review: ReviewState.Automated, platforms: [Platforms.Windows]));

        Assert.Equal("0.3.2", Plan(ResolutionRequest.Install("cobalt.docker")).Find("cobalt.docker")!.To!.ToString());
    }

    [Fact]
    public void ResolvesDependencyCyclesWithoutLooping()
    {
        Add("cycle.alpha", "Alpha", PluginTier.Unverified, V("1.0.0", review: ReviewState.Automated, deps: ("cycle.beta", "^1.0.0")));
        Add("cycle.beta", "Beta", PluginTier.Unverified, V("1.0.0", review: ReviewState.Automated, deps: ("cycle.alpha", "^1.0.0")));

        var plan = Plan(ResolutionRequest.Install("cycle.alpha"));

        Assert.Equal(["cycle.beta", "cycle.alpha"], plan.Operations.Select(op => op.PluginId));
    }

    [Fact]
    public void UpdatesOnlyWhatWasAskedFor()
    {
        WorkedExample();
        _plugins[0] = _plugins[0] with { Versions = [V("0.3.5"), .. _plugins[0].Versions] };
        Installed("runesmith.git", "0.3.3");
        Installed("harbor.issues", "1.2.0");

        var one = Plan(ResolutionRequest.Update("harbor.issues"));
        var all = Plan(ResolutionRequest.UpdateAll());

        Assert.Equal([("harbor.issues", "1.3.1")], one.Changes.Select(op => (op.PluginId, op.To!.ToString())));
        Assert.Equal([("runesmith.git", "0.3.5"), ("harbor.issues", "1.3.1")], all.Changes.Select(op => (op.PluginId, op.To!.ToString())));
    }

    [Fact]
    public void ReportsADowngradeWhenTheInstalledVersionCanNotStay()
    {
        Add("runesmith.git", "Runesmith Git", PluginTier.Official, V("0.3.0"), V("0.3.4"));
        Block("runesmith.git", "0.3.4");
        Installed("runesmith.git", "0.3.4");

        var operation = Plan(ResolutionRequest.Sync()).Find("runesmith.git")!;

        Assert.Equal((OperationKind.Downgrade, "0.3.0"), (operation.Kind, operation.To!.ToString()));
    }

    [Fact]
    public void RemovesAnInstalledPluginWhoseEveryVersionIsBlocked()
    {
        Add("fastcode.helper", "FastCode Helper", PluginTier.Unverified, V("1.0.1", review: ReviewState.Automated));
        Add("ember.themes", "Ember Themes", PluginTier.Verified, V("3.0.1"));
        _moderation.Add(Records.Moderation(1, new ModerationTarget { Kind = ModerationTargetKind.Publisher, Publisher = "fastcode" }, ModerationAction.Block));
        Installed("fastcode.helper", "1.0.1");

        var plan = Plan(ResolutionRequest.Install("ember.themes"));

        Assert.Equal("Remove fastcode.helper 1.0.1>- Unavailable; Install ember.themes ->3.0.1 Requested", Describe(plan));
    }

    [Fact]
    public void ReportsUnknownPluginsAndVersions()
    {
        WorkedExample();

        Assert.Equal("nobody.here is not in the hub.", Failure(ResolutionRequest.Install("nobody.here")).Message);
        Assert.Equal("Lumen Todo has no version 9.0.0.", Failure(ResolutionRequest.Install("lumen.todo", SemanticVersion.Parse("9.0.0"))).Message);
        Assert.Equal(ResolutionFailureKind.NotFound, Failure(ResolutionRequest.Install("nobody.here")).Kind);
    }

    [Fact]
    public void ExplainsADependencyOnAPluginThatIsNotInTheHub()
    {
        Add("lumen.todo", "Lumen Todo", PluginTier.Unverified, V("1.0.0", review: ReviewState.Automated, deps: ("ghost.plugin", "^1.0.0")));

        var failure = Failure(ResolutionRequest.Install("lumen.todo"));

        Assert.Contains("Lumen Todo needs ghost.plugin, which is not in the hub", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SuggestsNoFixThroughADependencyTheUserDidNotChoose()
    {
        Add("shared.core", "Shared Core", PluginTier.Unverified, V("1.0.0", review: ReviewState.Automated), V("2.0.0", review: ReviewState.Automated));
        Add("left.side", "Left Side", PluginTier.Unverified,
            V("1.0.0", review: ReviewState.Automated, deps: ("shared.core", "^1.0.0")), V("2.0.0", review: ReviewState.Automated, deps: ("shared.core", "^2.0.0")));
        Add("right.side", "Right Side", PluginTier.Unverified, V("1.0.0", review: ReviewState.Automated, deps: [("left.side", "^2.0.0"), ("shared.core", "^1.0.0")]));

        var failure = Failure(ResolutionRequest.Install("right.side"));

        Assert.Equal("Right Side needs Shared Core 1.x, and Left Side 2.0.0 needs Shared Core 2.x. No version of Shared Core satisfies both.", failure.Message);
        Assert.Equal(3, failure.Derivation.Count);
    }

    [Fact]
    public void FallsBackToTheDerivationForOtherConflicts()
    {
        Add("harbor.issues", "Harbor Issues", PluginTier.Verified, V("1.3.0"));
        Add("lumen.todo", "Lumen Todo", PluginTier.Unverified, V("1.0.0", review: ReviewState.Automated, deps: ("harbor.issues", "^5.0.0")));

        var failure = Failure(ResolutionRequest.Install("lumen.todo"));

        Assert.Equal("Because Lumen Todo needs Harbor Issues 5.x, which no version matches, and you asked to install Lumen Todo, no install plan exists.", failure.Message);
    }
}
