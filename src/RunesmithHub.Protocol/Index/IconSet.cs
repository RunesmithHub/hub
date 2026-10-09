using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Index;

/// <summary>A plugin's icon at each hosted size, as PNG.</summary>
public sealed record IconSet
{
    [JsonPropertyName("64")]
    public required HostedImage Size64 { get; init; }

    [JsonPropertyName("128")]
    public required HostedImage Size128 { get; init; }

    [JsonPropertyName("256")]
    public required HostedImage Size256 { get; init; }
}
