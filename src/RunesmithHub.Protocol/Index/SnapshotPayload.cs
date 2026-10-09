namespace RunesmithHub.Protocol.Index;

/// <summary>Pins every plugin record, the publisher list and the policy file. Signed by the publish role.</summary>
public sealed record SnapshotPayload : SignedPayload
{
    /// <summary>Gets the plugin records by plugin ID.</summary>
    public required IReadOnlyDictionary<string, FileMeta> Plugins { get; init; }

    public required FileMeta Publishers { get; init; }

    public required FileMeta Policy { get; init; }
}
