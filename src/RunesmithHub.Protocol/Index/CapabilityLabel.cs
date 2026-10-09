namespace RunesmithHub.Protocol.Index;

/// <summary>How a capability is described to users.</summary>
public sealed record CapabilityLabel
{
    public required string Label { get; init; }
}
