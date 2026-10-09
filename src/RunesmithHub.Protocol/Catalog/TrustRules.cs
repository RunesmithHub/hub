using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Protocol.Catalog;

/// <summary>A plugin's trust can't be higher than what it runs on: which tiers and versions a dependent may depend on.</summary>
public static class TrustRules
{
    /// <summary>Official plugins depend on official ones, verified on official or verified ones, unverified on any.</summary>
    public static bool MayDependOn(PluginTier dependent, PluginTier dependency) => dependent switch
    {
        PluginTier.Official => dependency == PluginTier.Official,
        PluginTier.Verified => dependency != PluginTier.Unverified,
        _ => true,
    };

    /// <summary>Official and verified plugins resolve only to reviewed versions; unverified ones to any published version.</summary>
    public static bool AcceptsVersion(PluginTier dependent, VersionRecord dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        return dependent == PluginTier.Unverified || dependency.Review.State == ReviewState.Reviewed;
    }
}
