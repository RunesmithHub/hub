using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Protocol.Index;

/// <summary>How much trust a plugin has earned.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<PluginTier>))]
public enum PluginTier
{
    Official,
    Verified,
    Unverified,
}
