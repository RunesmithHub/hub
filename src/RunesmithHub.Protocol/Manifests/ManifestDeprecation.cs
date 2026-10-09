namespace RunesmithHub.Protocol.Manifests;

/// <summary>The developer's deprecation notice.</summary>
public sealed record ManifestDeprecation
{
    public string? Reason { get; init; }

    public string? Replacement { get; init; }
}
