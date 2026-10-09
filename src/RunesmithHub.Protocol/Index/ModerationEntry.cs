namespace RunesmithHub.Protocol.Index;

/// <summary>A freeze or block of a publisher, a plugin or a range of a plugin's versions, as the index carries it.</summary>
public record ModerationEntry
{
    public required int Id { get; init; }

    public required ModerationTarget Target { get; init; }

    public required ModerationAction Action { get; init; }

    public required ModerationReason Reason { get; init; }

    /// <summary>Gets the short public explanation.</summary>
    public required string Note { get; init; }

    /// <summary>Gets a link to the security advisory, when there is one.</summary>
    public string? Advisory { get; init; }

    /// <summary>Gets what users should do.</summary>
    public string? Remediation { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
