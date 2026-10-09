namespace RunesmithHub.Protocol.Registry;

/// <summary>When a publisher was verified, and by which administrator's GitHub user ID.</summary>
public sealed record PublisherVerification
{
    public required DateTimeOffset At { get; init; }

    public required long By { get; init; }
}
