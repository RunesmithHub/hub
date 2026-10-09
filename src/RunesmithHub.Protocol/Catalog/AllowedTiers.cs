using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Protocol.Catalog;

/// <summary>The user's setting for which tiers may be installed.</summary>
public enum AllowedTiers
{
    /// <summary>Every tier, with warnings for unverified plugins.</summary>
    All,
    OfficialAndVerified,
    OfficialOnly,
}

/// <summary>Applies <see cref="AllowedTiers"/>.</summary>
public static class AllowedTiersExtensions
{
    public static bool Allows(this AllowedTiers setting, PluginTier tier) => setting switch
    {
        AllowedTiers.OfficialOnly => tier == PluginTier.Official,
        AllowedTiers.OfficialAndVerified => tier != PluginTier.Unverified,
        _ => true,
    };
}
