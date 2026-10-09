namespace RunesmithHub.Protocol.Manifests;

/// <summary>The paths of a plugin's two project files, relative to the plugin root.</summary>
public sealed record ManifestProjects
{
    public string? Contracts { get; init; }

    public string? Implementation { get; init; }
}
