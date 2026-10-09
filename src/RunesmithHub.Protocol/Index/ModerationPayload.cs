namespace RunesmithHub.Protocol.Index;

/// <summary>Every active freeze and block. Signed by the moderation role.</summary>
public sealed record ModerationPayload : SignedPayload
{
    public required IReadOnlyList<ModerationEntry> Entries { get; init; }
}
