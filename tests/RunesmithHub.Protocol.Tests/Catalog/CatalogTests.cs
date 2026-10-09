using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Tests.Support;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Tests.Catalog;

public sealed class CatalogTests
{
    private static readonly RunesmithHost Host = new(SemanticVersion.Parse("0.1.0"), SemanticVersion.Parse("0.1.0"), Platforms.Linux);

    private static EffectiveState StateOf(
        VersionState version, PluginState plugin = PluginState.Listed, PublisherState publisher = PublisherState.Active, params ModerationEntry[] moderation)
    {
        var record = Records.Plugin("harbor.issues", "Harbor Issues", PluginTier.Verified, [Records.Version("1.3.1", version)], plugin);
        var catalog = Records.Catalog([record], moderation, Records.Publisher("harbor", publisher));
        return catalog.GetEffectiveState("harbor.issues", SemanticVersion.Parse("1.3.1"))!;
    }

    private static ModerationTarget Versions(string range) =>
        new() { Kind = ModerationTargetKind.Versions, Plugin = "harbor.issues", Range = VersionRange.Parse(range) };

    [Theory]
    [InlineData(VersionState.Published, PluginState.Listed, VersionState.Published)]
    [InlineData(VersionState.Published, PluginState.Deprecated, VersionState.Published)]
    [InlineData(VersionState.Yanked, PluginState.Listed, VersionState.Yanked)]
    [InlineData(VersionState.Published, PluginState.Deleted, VersionState.Deleted)]
    [InlineData(VersionState.Yanked, PluginState.Deleted, VersionState.Deleted)]
    [InlineData(VersionState.Deleted, PluginState.Frozen, VersionState.Frozen)]
    [InlineData(VersionState.Frozen, PluginState.Deleted, VersionState.Frozen)]
    [InlineData(VersionState.Published, PluginState.Blocked, VersionState.Blocked)]
    [InlineData(VersionState.Blocked, PluginState.Listed, VersionState.Blocked)]
    [InlineData(VersionState.Review, PluginState.Listed, VersionState.Review)]
    [InlineData(VersionState.Held, PluginState.Listed, VersionState.Held)]
    [InlineData(VersionState.Held, PluginState.Frozen, VersionState.Frozen)]
    public void KeepsTheMostRestrictiveOfTheVersionAndPluginStates(VersionState version, PluginState plugin, VersionState expected)
    {
        Assert.Equal(expected, StateOf(version, plugin).State);
    }

    [Fact]
    public void ABlockedPublisherBlocksEveryVersion()
    {
        Assert.Equal(VersionState.Blocked, StateOf(VersionState.Published, publisher: PublisherState.Blocked).State);
        Assert.Equal(VersionState.Published, StateOf(VersionState.Published, publisher: PublisherState.Suspended).State);
    }

    [Fact]
    public void AModerationEntryWinsAndIsReported()
    {
        var freeze = Records.Moderation(3, new ModerationTarget { Kind = ModerationTargetKind.Plugin, Plugin = "harbor.issues" }, ModerationAction.Freeze);
        var block = Records.Moderation(4, Versions("1.3.1"), ModerationAction.Block, ModerationReason.Vulnerability);

        var frozen = StateOf(VersionState.Published, moderation: freeze);
        var blocked = StateOf(VersionState.Published, moderation: [freeze, block]);

        Assert.Equal(VersionState.Frozen, frozen.State);
        Assert.Same(freeze, frozen.Moderation);
        Assert.Equal(VersionState.Blocked, blocked.State);
        Assert.Same(block, blocked.Moderation);
        Assert.False(blocked.IsInstallable);
    }

    [Fact]
    public void SnapshotStatesCanNotLoosenAModerationEntry()
    {
        var freeze = Records.Moderation(3, Versions(">=1.0.0 <2.0.0"), ModerationAction.Freeze);

        Assert.Equal(VersionState.Frozen, StateOf(VersionState.Published, moderation: freeze).State);
        Assert.Equal(VersionState.Blocked, StateOf(VersionState.Blocked, moderation: freeze).State);
    }

    [Fact]
    public void AModerationRangeIncludesPreReleasesAndOnlyTargetsItsPlugin()
    {
        var block = Records.Moderation(1, Versions(">=1.3.0 <1.4.0"), ModerationAction.Block);
        var record = Records.Plugin("harbor.issues", "Harbor Issues", PluginTier.Verified, [Records.Version("1.3.5-beta.1"), Records.Version("1.4.0")]);
        var other = Records.Plugin("harbor.blame", "Harbor Blame", PluginTier.Verified, [Records.Version("1.3.1")]);
        var catalog = Records.Catalog([record, other], [block]);

        Assert.Equal(VersionState.Blocked, catalog.GetEffectiveState("harbor.issues", SemanticVersion.Parse("1.3.5-beta.1"))!.State);
        Assert.Equal(VersionState.Published, catalog.GetEffectiveState("harbor.issues", SemanticVersion.Parse("1.4.0"))!.State);
        Assert.Equal(VersionState.Published, catalog.GetEffectiveState("harbor.blame", SemanticVersion.Parse("1.3.1"))!.State);
        Assert.Null(catalog.GetEffectiveState("harbor.issues", SemanticVersion.Parse("9.9.9")));
    }

    [Fact]
    public void APublisherModerationEntryCoversEveryPluginOfThePublisher()
    {
        var block = Records.Moderation(1, new ModerationTarget { Kind = ModerationTargetKind.Publisher, Publisher = "fastcode" }, ModerationAction.Block);
        var catalog = Records.Catalog([Records.Plugin("fastcode.helper", "FastCode Helper", PluginTier.Unverified, [Records.Version("1.0.1")])], [block]);

        Assert.Equal(VersionState.Blocked, catalog.GetEffectiveState("fastcode.helper", SemanticVersion.Parse("1.0.1"))!.State);
    }

    [Theory]
    [InlineData(VersionState.Published, 0)]
    [InlineData(VersionState.Review, 1)]
    [InlineData(VersionState.Held, 1)]
    [InlineData(VersionState.Yanked, 2)]
    [InlineData(VersionState.Deleted, 3)]
    [InlineData(VersionState.Frozen, 4)]
    [InlineData(VersionState.Blocked, 5)]
    public void OrdersStatesByRestrictiveness(VersionState state, int rank)
    {
        Assert.Equal(rank, EffectiveState.Restrictiveness(state));
    }

    [Theory]
    [InlineData("0.4.2", "0.4.0", ">=0.4.0 <0.5.0", true)]
    [InlineData("0.4.2", "0.4.0", ">=0.4.3 <0.5.0", false)]
    [InlineData("0.4.2", "0.4.0", ">=0.3.0 <0.4.0", false)]
    [InlineData("0.4.2", "0.4.0", ">=0.3.0 <0.5.0", false)]
    [InlineData("0.4.2", "0.4.0", ">=0.4.0 <0.4.2", false)]
    [InlineData("1.2.0", "1.0.0", ">=1.1.0 <2.0.0", true)]
    public void AppliesTheRunesmithApiRule(string api, string oldest, string range, bool runs)
    {
        var host = new RunesmithHost(SemanticVersion.Parse(api), SemanticVersion.Parse(oldest), null);

        Assert.Equal(runs, host.SupportsApi(VersionRange.Parse(range)));
    }

    [Fact]
    public void ListsInstallableVersionsHighestFirst()
    {
        var record = Records.Plugin("lumen.todo", "Lumen Todo", PluginTier.Unverified,
        [
            Records.Version("0.9.2"),
            Records.Version("1.0.0"),
            Records.Version("1.1.0", VersionState.Held),
            Records.Version("1.0.1", VersionState.Yanked),
            Records.Version("1.2.0-beta.1"),
            Records.Version("0.8.0", api: "^0.2.0"),
            Records.Version("0.9.5", platforms: [Platforms.Windows]),
        ]);
        var catalog = Records.Catalog([record]);

        Assert.Equal(["1.0.0", "0.9.2"], catalog.GetInstallableVersions("lumen.todo", Host).Select(v => v.Version.ToString()));
        Assert.Equal(["1.2.0-beta.1", "1.0.0", "0.9.2"], catalog.GetInstallableVersions("lumen.todo", Host, allowPreReleases: true).Select(v => v.Version.ToString()));
        Assert.Empty(catalog.GetInstallableVersions("lumen.todo", Host, AllowedTiers.OfficialAndVerified));
        Assert.Empty(catalog.GetInstallableVersions("nobody.here", Host));
    }

    [Theory]
    [InlineData(AllowedTiers.All, PluginTier.Unverified, true)]
    [InlineData(AllowedTiers.OfficialAndVerified, PluginTier.Unverified, false)]
    [InlineData(AllowedTiers.OfficialAndVerified, PluginTier.Verified, true)]
    [InlineData(AllowedTiers.OfficialOnly, PluginTier.Verified, false)]
    [InlineData(AllowedTiers.OfficialOnly, PluginTier.Official, true)]
    public void AppliesTheTierSetting(AllowedTiers setting, PluginTier tier, bool allowed)
    {
        Assert.Equal(allowed, setting.Allows(tier));
    }

    [Theory]
    [InlineData(PluginTier.Official, PluginTier.Official, true)]
    [InlineData(PluginTier.Official, PluginTier.Verified, false)]
    [InlineData(PluginTier.Verified, PluginTier.Official, true)]
    [InlineData(PluginTier.Verified, PluginTier.Verified, true)]
    [InlineData(PluginTier.Verified, PluginTier.Unverified, false)]
    [InlineData(PluginTier.Unverified, PluginTier.Unverified, true)]
    public void AppliesTheTrustRulesForTiers(PluginTier dependent, PluginTier dependency, bool allowed)
    {
        Assert.Equal(allowed, TrustRules.MayDependOn(dependent, dependency));
    }

    [Fact]
    public void OnlyUnverifiedPluginsAcceptAutomatedVersions()
    {
        var automated = Records.Version("1.0.0", review: ReviewState.Automated);

        Assert.True(TrustRules.AcceptsVersion(PluginTier.Unverified, automated));
        Assert.False(TrustRules.AcceptsVersion(PluginTier.Verified, automated));
        Assert.False(TrustRules.AcceptsVersion(PluginTier.Official, automated));
        Assert.True(TrustRules.AcceptsVersion(PluginTier.Official, Records.Version("1.0.0")));
    }
}
