namespace RunesmithHub.Protocol.Manifests;

/// <summary>A screenshot's path and caption.</summary>
public sealed record ManifestGalleryImage
{
    public string? Path { get; init; }

    public string? Caption { get; init; }
}
