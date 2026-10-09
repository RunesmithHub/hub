namespace RunesmithHub.Protocol.Manifests;

/// <summary>
/// The <c>plugin.json</c> a developer writes. Every field is optional here so that <see cref="ManifestValidator"/> can report each missing
/// or invalid one; a manifest is only usable once it validates.
/// </summary>
public sealed record DeveloperManifest
{
    /// <summary>The name of the manifest file.</summary>
    public const string FileName = "plugin.json";

    /// <summary>The <c>schemaVersion</c> this library reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;

    public int? SchemaVersion { get; init; }

    public string? Id { get; init; }

    public string? Name { get; init; }

    public string? Version { get; init; }

    public string? Summary { get; init; }

    public IReadOnlyList<string>? Authors { get; init; }

    /// <summary>Gets the SPDX license expression.</summary>
    public string? License { get; init; }

    /// <summary>Gets the GitHub repository URL.</summary>
    public string? Repository { get; init; }

    public string? Homepage { get; init; }

    /// <summary>Gets the Runesmith API range as written.</summary>
    public string? RunesmithApi { get; init; }

    public ManifestProjects? Projects { get; init; }

    public IReadOnlyList<ManifestDependency>? Dependencies { get; init; }

    public IReadOnlyList<ManifestCapability>? Capabilities { get; init; }

    public IReadOnlyList<string>? NetworkHosts { get; init; }

    public IReadOnlyList<string>? Platforms { get; init; }

    /// <summary>Gets the icon's path, relative to the plugin root.</summary>
    public string? Icon { get; init; }

    public string? Banner { get; init; }

    public IReadOnlyList<ManifestGalleryImage>? Gallery { get; init; }

    public IReadOnlyList<string>? Categories { get; init; }

    public IReadOnlyList<string>? Keywords { get; init; }

    public ManifestDeprecation? Deprecated { get; init; }
}
