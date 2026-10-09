namespace RunesmithHub.Protocol.Registry;

/// <summary>Package size limits raised above the defaults.</summary>
public sealed record SizeLimits
{
    /// <summary>Gets the most bytes the package file may have.</summary>
    public long? PackageBytes { get; init; }

    /// <summary>Gets the most bytes the package's entries may have together, uncompressed.</summary>
    public long? UnpackedBytes { get; init; }
}
