namespace RunesmithHub.Tool.Build;

/// <summary>
/// A file from the implementation's build output that goes into the package beside the plugin's own assemblies: from a NuGet package, or
/// built from the plugin's repository when <see cref="PackageId"/> is null.
/// </summary>
/// <param name="PackagePath">Where it goes in the package, such as <c>lib/Markdig.dll</c>, <c>lib/BuildHost-netcore/Helper.dll</c> or <c>lib/native/linux-x64/libgit2.so</c>.</param>
/// <param name="PathInPackage">The file's path inside its NuGet package, to compare the bytes with.</param>
internal sealed record BundleItem(string PackagePath, string SourceFile, string? PackageId, string? PackageVersion, string? PathInPackage)
{
    public bool IsNative => PackagePath.StartsWith(Protocol.Packaging.PackageEntryRules.NativePrefix, StringComparison.Ordinal);

    public string FileName => PackagePath[(PackagePath.LastIndexOf('/') + 1)..];

    /// <summary>Gets its path under <c>lib/</c>, such as <c>Markdig.dll</c> or <c>BuildHost-netcore/Helper.dll</c>.</summary>
    public string LibPath => PackagePath["lib/".Length..];
}
