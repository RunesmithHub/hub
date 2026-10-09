using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Index;

/// <summary>A plugin's banner at each hosted width, as WebP.</summary>
public sealed record BannerSet
{
    [JsonPropertyName("640")]
    public required HostedImage Width640 { get; init; }

    [JsonPropertyName("1280")]
    public required HostedImage Width1280 { get; init; }
}
