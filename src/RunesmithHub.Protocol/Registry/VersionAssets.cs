using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Protocol.Registry;

/// <summary>The hosted images and the contracts package of a version, which the plugin record and the hub feed take from it.</summary>
public sealed record VersionAssets
{
    public required IconSet Icon { get; init; }

    public BannerSet? Banner { get; init; }

    public IReadOnlyList<GalleryItem> Gallery { get; init; } = [];

    /// <summary>Gets the contracts NuGet package pushed to the hub feed.</summary>
    public required AssetFile ContractsPackage { get; init; }
}
