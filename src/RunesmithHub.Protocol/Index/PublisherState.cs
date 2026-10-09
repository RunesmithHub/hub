using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Protocol.Index;

/// <summary>A publisher's state.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<PublisherState>))]
public enum PublisherState
{
    Active,
    Suspended,
    Blocked,
}
