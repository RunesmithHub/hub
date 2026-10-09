using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Protocol.Index;

/// <summary>A version's state in the index.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<VersionState>))]
public enum VersionState
{
    Review,
    Held,
    Published,
    Yanked,
    Frozen,
    Blocked,
    Deleted,
}
