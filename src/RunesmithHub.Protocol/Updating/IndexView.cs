using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Signing;

namespace RunesmithHub.Protocol.Updating;

/// <summary>A verified, consistent view of the index: the payloads of every signed file and every record they pin.</summary>
public sealed class IndexView
{
    internal IndexView(
        RootPayload root, TimestampPayload timestamp, SnapshotPayload snapshot, ModerationPayload moderation,
        IReadOnlyDictionary<string, PluginRecord> plugins, PublisherList publishers, PolicyPayload policy)
    {
        Root = root;
        Timestamp = timestamp;
        Snapshot = snapshot;
        Moderation = moderation;
        Plugins = plugins;
        Publishers = publishers;
        Policy = policy;
    }

    /// <summary>Gets the root the view was verified with.</summary>
    public RootPayload Root { get; }

    public TimestampPayload Timestamp { get; }

    public SnapshotPayload Snapshot { get; }

    public ModerationPayload Moderation { get; }

    /// <summary>Gets the plugin records by plugin ID.</summary>
    public IReadOnlyDictionary<string, PluginRecord> Plugins { get; }

    public PublisherList Publishers { get; }

    public PolicyPayload Policy { get; }

    /// <summary>Gets when the first of the root, timestamp, snapshot and moderation list expires.</summary>
    public DateTimeOffset ExpiresAt => new[] { Root.Expires, Timestamp.Expires, Snapshot.Expires, Moderation.Expires }.Min();

    /// <summary>Gets whether any signed file of the view has expired at the given time.</summary>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Rebuilds the view from files a trust store kept, which were verified when they were saved.</summary>
    /// <exception cref="InvalidDataException">A file can't be read.</exception>
    public static IndexView FromTrustedFiles(ReadOnlyMemory<byte> root, TrustedFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return new IndexView(
            ReadPayload<RootPayload>(root),
            ReadPayload<TimestampPayload>(files.Timestamp),
            ReadPayload<SnapshotPayload>(files.Snapshot),
            ReadPayload<ModerationPayload>(files.Moderation),
            files.Plugins.ToDictionary(pair => pair.Key, pair => IndexJson.Deserialize<PluginRecord>(pair.Value.Span), StringComparer.Ordinal),
            IndexJson.Deserialize<PublisherList>(files.Publishers.Span),
            IndexJson.Deserialize<PolicyPayload>(files.Policy.Span));
    }

    private static T ReadPayload<T>(ReadOnlyMemory<byte> envelope)
        where T : SignedPayload => IndexJson.Deserialize<T>(IndexJson.Deserialize<DsseEnvelope>(envelope.Span).Payload.Span);
}
