namespace RunesmithHub.Protocol.Index;

/// <summary>The capabilities a version declares, with reasons, and the ones the analysis detected.</summary>
public sealed record CapabilityReport
{
    public required IReadOnlyList<DeclaredCapability> Declared { get; init; }

    public required IReadOnlyList<string> Detected { get; init; }
}
