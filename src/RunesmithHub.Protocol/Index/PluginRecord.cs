namespace RunesmithHub.Protocol.Index;

/// <summary>Everything the index says about one plugin: its registration, listing and every version. Pinned by the snapshot.</summary>
public sealed record PluginRecord : IndexDocument
{
    public required string Id { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the publisher name.</summary>
    public required string Publisher { get; init; }

    public required PluginTier Tier { get; init; }

    public required PluginState State { get; init; }

    public required RecordRepository Repository { get; init; }

    public required IReadOnlyList<GitHubAccount> Maintainers { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<string> Categories { get; init; }

    public required IReadOnlyList<string> Keywords { get; init; }

    public Deprecation? Deprecated { get; init; }

    public required IconSet Icon { get; init; }

    public BannerSet? Banner { get; init; }

    public required IReadOnlyList<GalleryItem> Gallery { get; init; }

    /// <summary>Gets the version records, highest version first.</summary>
    public required IReadOnlyList<VersionRecord> Versions { get; init; }

    /// <summary>Finds a version record.</summary>
    public VersionRecord? FindVersion(Versioning.SemanticVersion version) => Versions.FirstOrDefault(record => record.Version == version);
}
