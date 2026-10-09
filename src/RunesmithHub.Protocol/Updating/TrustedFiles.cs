namespace RunesmithHub.Protocol.Updating;

/// <summary>The exact bytes of every verified index file, so the view can be rebuilt and versions compared at the next update.</summary>
public sealed record TrustedFiles
{
    public required ReadOnlyMemory<byte> Timestamp { get; init; }

    public required ReadOnlyMemory<byte> Snapshot { get; init; }

    public required ReadOnlyMemory<byte> Moderation { get; init; }

    public required ReadOnlyMemory<byte> Publishers { get; init; }

    public required ReadOnlyMemory<byte> Policy { get; init; }

    /// <summary>Gets the plugin records by plugin ID.</summary>
    public required IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Plugins { get; init; }
}
