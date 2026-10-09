namespace RunesmithHub.Protocol.Manifests;

/// <summary>A dependency as the developer writes it.</summary>
public sealed record ManifestDependency
{
    public string? Id { get; init; }

    public string? Range { get; init; }
}
