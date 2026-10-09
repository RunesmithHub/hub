namespace RunesmithHub.Protocol.Manifests;

/// <summary>A capability as the developer declares it, with the reason users see.</summary>
public sealed record ManifestCapability
{
    public string? Id { get; init; }

    public string? Reason { get; init; }
}
