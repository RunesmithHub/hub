using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Protocol.Index;

/// <summary>A plugin's state in the index.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<PluginState>))]
public enum PluginState
{
    Listed,
    Deprecated,
    Frozen,
    Blocked,
    Deleted,
}
