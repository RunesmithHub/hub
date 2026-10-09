using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Index;

/// <summary>A screenshot at each hosted width, as WebP, with its caption.</summary>
public sealed record GalleryItem
{
    public required string Caption { get; init; }

    [JsonPropertyName("640")]
    public required HostedImage Width640 { get; init; }

    [JsonPropertyName("1280")]
    public required HostedImage Width1280 { get; init; }
}
