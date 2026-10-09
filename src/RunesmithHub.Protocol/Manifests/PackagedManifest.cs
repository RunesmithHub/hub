using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Manifests;

/// <summary>
/// The <c>plugin.json</c> inside a package: the developer's listing fields with normalised ranges, plus the assemblies and build the hub
/// adds.
/// </summary>
public sealed record PackagedManifest
{
    public required int SchemaVersion { get; init; }

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required SemanticVersion Version { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<string> Authors { get; init; }

    public required string License { get; init; }

    public required string Repository { get; init; }

    public string? Homepage { get; init; }

    /// <summary>Gets the normalised Runesmith API range.</summary>
    public required VersionRange RunesmithApi { get; init; }

    // Source-generated JSON passes null for omitted init-only properties, so the setters restore the defaults.
    /// <summary>Gets the dependencies with normalised ranges.</summary>
    public IReadOnlyList<PluginDependency> Dependencies { get; init => field = value ?? []; } = [];

    public IReadOnlyList<DeclaredCapability> Capabilities { get; init => field = value ?? []; } = [];

    public IReadOnlyList<string> NetworkHosts { get; init => field = value ?? []; } = [];

    /// <summary>Gets the supported platforms; empty means every platform.</summary>
    public IReadOnlyList<string> Platforms { get; init => field = value ?? []; } = [];

    public required IReadOnlyList<string> Categories { get; init; }

    public IReadOnlyList<string> Keywords { get; init => field = value ?? []; } = [];

    public Deprecation? Deprecated { get; init; }

    public required PackagedAssemblies Assemblies { get; init; }

    public required PackageBuild Build { get; init; }

    /// <summary>Creates the packaged manifest from a developer manifest that passed <see cref="ManifestValidator"/>.</summary>
    /// <exception cref="ArgumentException">The manifest is not valid.</exception>
    public static PackagedManifest Create(DeveloperManifest manifest, PackagedAssemblies assemblies, PackageBuild build)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var problems = ManifestValidator.Validate(manifest, ManifestValidationOptions.Default);
        if (problems.Count > 0)
            throw new ArgumentException($"The manifest is not valid: {problems[0]}", nameof(manifest));

        return new PackagedManifest
        {
            SchemaVersion = manifest.SchemaVersion!.Value,
            Id = manifest.Id!,
            Name = manifest.Name!,
            Version = SemanticVersion.Parse(manifest.Version!),
            Summary = manifest.Summary!,
            Authors = manifest.Authors!,
            License = manifest.License!,
            Repository = manifest.Repository!,
            Homepage = manifest.Homepage,
            RunesmithApi = VersionRange.Parse(manifest.RunesmithApi!).Normalize(),
            Dependencies = [.. (manifest.Dependencies ?? []).Select(d => new PluginDependency { Id = d.Id!, Range = VersionRange.Parse(d.Range!).Normalize() })],
            Capabilities = [.. (manifest.Capabilities ?? []).Select(c => new DeclaredCapability { Id = c.Id!, Reason = c.Reason! })],
            NetworkHosts = manifest.NetworkHosts ?? [],
            Platforms = manifest.Platforms ?? [],
            Categories = manifest.Categories!,
            Keywords = manifest.Keywords ?? [],
            Deprecated = manifest.Deprecated is { } deprecated ? new Deprecation { Reason = deprecated.Reason!, Replacement = deprecated.Replacement } : null,
            Assemblies = assemblies,
            Build = build,
        };
    }
}
