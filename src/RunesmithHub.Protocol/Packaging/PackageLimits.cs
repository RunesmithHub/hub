using RunesmithHub.Protocol.Registry;

namespace RunesmithHub.Protocol.Packaging;

/// <summary>The size limits a package must stay within.</summary>
public sealed record PackageLimits
{
    public const int MaxEntries = 500;
    public const int MaxCompressionRatio = 100;

    public static PackageLimits Default { get; } = new();

    /// <summary>Gets the most bytes the package file may have; 50 MiB unless the registration raises it.</summary>
    public long MaxPackageBytes { get; init; } = 50L * 1024 * 1024;

    /// <summary>Gets the most bytes all entries may have together, uncompressed; 150 MiB unless the registration raises it.</summary>
    public long MaxUnpackedBytes { get; init; } = 150L * 1024 * 1024;

    /// <summary>Applies the raised limits of a registration or publisher, never going below the defaults.</summary>
    public static PackageLimits From(SizeLimits? limits) => new()
    {
        MaxPackageBytes = Math.Max(Default.MaxPackageBytes, limits?.PackageBytes ?? 0),
        MaxUnpackedBytes = Math.Max(Default.MaxUnpackedBytes, limits?.UnpackedBytes ?? 0),
    };
}
