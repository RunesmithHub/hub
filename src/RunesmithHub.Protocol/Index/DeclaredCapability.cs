namespace RunesmithHub.Protocol.Index;

/// <summary>A capability the developer declared, with the reason users see.</summary>
public sealed record DeclaredCapability
{
    public required string Id { get; init; }

    public required string Reason { get; init; }
}
