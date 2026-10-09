using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Build;

/// <summary>A NuGet package whose files the package bundles, with what its notices and the SBOM say about it.</summary>
internal sealed record BundledPackage(string Id, string Version, string ContentHash, PackageLicense License, string? Copyright, string? ProjectUrl, string? Authors)
{
    /// <summary>Describes a package the lock files pin from the package file itself.</summary>
    public static BundledPackage From(LockedPackage pinned, NuGetPackage package)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(package);
        return new BundledPackage(pinned.Id, pinned.Version, pinned.ContentHash, package.License, package.Copyright, package.ProjectUrl, package.Authors);
    }
}
