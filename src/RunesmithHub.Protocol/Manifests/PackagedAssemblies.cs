using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Manifests;

/// <summary>Every assembly in a package's <c>lib/</c> folder with its SHA-256.</summary>
public sealed record PackagedAssemblies
{
    public required AssemblyFile Contracts { get; init; }

    public required AssemblyFile Implementation { get; init; }

    // Source-generated JSON passes null for omitted init-only properties, so the setters restore the defaults.
    /// <summary>Gets the other assemblies: third-party ones with the NuGet packages they came from, and ones built from the repository.</summary>
    public IReadOnlyList<BundledAssembly> Bundled { get; init => field = value ?? []; } = [];

    /// <summary>Gets every assembly, contracts and implementation first.</summary>
    [JsonIgnore]
    public IEnumerable<(string File, string Sha256)> All =>
        [(Contracts.File, Contracts.Sha256), (Implementation.File, Implementation.Sha256), .. Bundled.Select(b => (b.File, b.Sha256))];
}
