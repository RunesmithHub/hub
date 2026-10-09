namespace RunesmithHub.Protocol.Index;

/// <summary>The GitHub user or organization a publisher is bound to.</summary>
public sealed record PublisherOwner
{
    public required long Id { get; init; }

    public required string Login { get; init; }

    public required OwnerType Type { get; init; }
}
