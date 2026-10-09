namespace RunesmithHub.Protocol.Index;

/// <summary>A publisher as the index lists it.</summary>
public sealed record PublisherEntry
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required PublisherOwner Owner { get; init; }

    public required PublisherState State { get; init; }

    public required bool Verified { get; init; }

    public DateTimeOffset? VerifiedAt { get; init; }
}
