namespace RunesmithHub.Protocol.Index;

/// <summary>Names the current snapshot and moderation list. Signed by the timestamp role.</summary>
public sealed record TimestampPayload : SignedPayload
{
    public required FileMeta Snapshot { get; init; }

    public required FileMeta Moderation { get; init; }
}
