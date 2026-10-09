namespace RunesmithHub.Protocol.Index;

/// <summary>The four roles of the index and their keys.</summary>
public sealed record RootRoles
{
    /// <summary>Gets the keys that sign new roots.</summary>
    public required RoleKeys Root { get; init; }

    /// <summary>Gets the keys that sign the snapshot.</summary>
    public required RoleKeys Publish { get; init; }

    /// <summary>Gets the keys that sign the moderation list.</summary>
    public required RoleKeys Moderation { get; init; }

    /// <summary>Gets the keys that sign the timestamp.</summary>
    public required RoleKeys Timestamp { get; init; }
}
