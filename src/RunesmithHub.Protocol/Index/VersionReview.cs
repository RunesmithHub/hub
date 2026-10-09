namespace RunesmithHub.Protocol.Index;

/// <summary>Whether and when a person reviewed a version.</summary>
public sealed record VersionReview
{
    public required ReviewState State { get; init; }

    /// <summary>Gets the reviewer's GitHub user ID.</summary>
    public long? By { get; init; }

    public DateTimeOffset? At { get; init; }
}
