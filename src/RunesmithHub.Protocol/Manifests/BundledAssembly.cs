using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Manifests;

/// <summary>
/// A file in a package's <c>lib/</c> folder besides the contracts and implementation, such as an assembly or a file a package copies to the
/// build output: from a NuGet package, or, when <see cref="Package"/> is absent, built from the plugin's own repository at the build commit.
/// </summary>
public sealed record BundledAssembly
{
    /// <summary>Gets its path under <c>lib/</c>, such as <c>Markdig.dll</c> or <c>BuildHost-netcore/Helper.dll</c>.</summary>
    public required string File { get; init; }

    public required string Sha256 { get; init; }

    /// <summary>Gets the NuGet package ID, or null when the assembly was built from the plugin's repository.</summary>
    public string? Package { get; init; }

    /// <summary>Gets the NuGet package version, or null when the assembly was built from the plugin's repository.</summary>
    public string? PackageVersion { get; init; }

    /// <summary>Gets whether the assembly was built from the plugin's repository rather than taken from a NuGet package.</summary>
    [JsonIgnore]
    [MemberNotNullWhen(false, nameof(Package), nameof(PackageVersion))]
    public bool IsBuiltFromRepository => Package is null || PackageVersion is null;
}
