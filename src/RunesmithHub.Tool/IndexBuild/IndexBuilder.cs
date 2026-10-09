using System.Globalization;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Signing;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.IndexBuild;

/// <summary>A new index state and what the build did.</summary>
internal sealed record IndexBuildResult(IndexState State, IReadOnlyList<string> Notes);

/// <summary>
/// Builds new index generations on top of a base state. Each operation signs only with the role key it needs from <see cref="OnlineKeys"/>;
/// a timestamp is added whenever the timestamp key is present and both the snapshot and the moderation list exist.
/// </summary>
internal sealed class IndexBuilder(TimeProvider time)
{
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromDays(90);
    private static readonly TimeSpan ModerationLifetime = TimeSpan.FromDays(90);
    private static readonly TimeSpan TimestampLifetime = TimeSpan.FromDays(3);
    private static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// Writes the plugin records, publisher list and policy from the registry and signs a new snapshot when any of them changed, when the
    /// current snapshot expires within 30 days, or when there is none. In bootstrap, the moderation list is signed too if its key is present.
    /// </summary>
    public IndexBuildResult Publish(IndexState basis, RegistryState registry, IReadOnlyList<Publication> publications, OnlineKeys keys)
    {
        var now = Now();
        var notes = new List<string>();
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var byKey = publications.ToDictionary(publication => publication.Key, StringComparer.Ordinal);

        var plugins = new Dictionary<string, FileMeta>(StringComparer.Ordinal);
        foreach (var record in IndexContent.PluginRecords(registry, byKey, id => basis.PluginRecord(id)?.Record))
        {
            var previous = basis.PluginRecord(record.Id);
            var (meta, bytes) = Versioned(record, previous?.Record.Version, previous?.Bytes, (r, version) => r with { Version = version });
            files[IndexPaths.Plugin(meta.Sha256)] = bytes;
            plugins[record.Id] = meta;
        }

        var previousPublishers = basis.PublishersBytes;
        var (publishers, publishersBytes) = Versioned(IndexContent.Publishers(registry), previousPublishers is null ? null : IndexJson.Deserialize<PublisherList>(previousPublishers).Version,
            previousPublishers, (list, version) => list with { Version = version });
        files[IndexPaths.Publishers(publishers.Sha256)] = publishersBytes;
        var previousPolicy = basis.PolicyBytes;
        var (policy, policyBytes) = Versioned(IndexContent.Policy(registry), previousPolicy is null ? null : IndexJson.Deserialize<PolicyPayload>(previousPolicy).Version,
            previousPolicy, (payload, version) => payload with { Version = version });
        files[IndexPaths.Policy(policy.Sha256)] = policyBytes;

        var state = basis;
        var current = basis.Snapshot?.Payload;
        if (current is not null && SamePins(current, plugins, publishers, policy) && !Renewable(current.Expires, now))
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"Snapshot {current.Version} is up to date; nothing new to sign."));
        }
        else
        {
            var snapshot = new SnapshotPayload
            {
                Type = PayloadTypes.Snapshot,
                Version = (current?.Version ?? 0) + 1,
                Expires = now + SnapshotLifetime,
                Plugins = plugins,
                Publishers = publishers,
                Policy = policy,
            };
            var path = Add(files, IndexPaths.Snapshot, Sign(snapshot, keys.Require(OnlineRole.Publish)));
            state = basis.With(files, snapshot: new SignedFile<SnapshotPayload>(path, snapshot));
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"Signed snapshot {snapshot.Version} with {plugins.Count} plugins and {publications.Count} new publications."));
        }

        if (state.Moderation is null && keys.Find(OnlineRole.Moderation) is not null)
            state = SignModeration(state, registry, keys, now, notes);
        return Timestamped(state, keys, now, notes);
    }

    /// <summary>
    /// Signs a new moderation list from the active moderation entries when they changed, when the current list expires within 30 days,
    /// or when there is none.
    /// </summary>
    public IndexBuildResult Moderate(IndexState basis, RegistryState registry, OnlineKeys keys)
    {
        var now = Now();
        var notes = new List<string>();
        var current = basis.Moderation?.Payload;
        var state = current is not null && current.Entries.SequenceEqual(IndexContent.ModerationEntries(registry)) && !Renewable(current.Expires, now)
            ? Note(basis, notes, string.Create(CultureInfo.InvariantCulture, $"Moderation list {current.Version} is up to date; nothing new to sign."))
            : SignModeration(basis, registry, keys, now, notes);
        return Timestamped(state, keys, now, notes);
    }

    /// <summary>Signs a timestamp that names the base's current snapshot and moderation list.</summary>
    /// <exception cref="ToolException">The base has no snapshot or moderation list, or the timestamp key is not set.</exception>
    public IndexBuildResult Timestamp(IndexState basis, OnlineKeys keys)
    {
        if (basis.Snapshot is null || basis.Moderation is null)
            throw new ToolException("The index has no snapshot or no moderation list yet, so there is nothing to timestamp; publish and moderate first.");
        var notes = new List<string>();
        keys.Require(OnlineRole.Timestamp);
        return Timestamped(basis, keys, Now(), notes);
    }

    private static IndexState SignModeration(IndexState basis, RegistryState registry, OnlineKeys keys, DateTimeOffset now, List<string> notes)
    {
        var moderation = new ModerationPayload
        {
            Type = PayloadTypes.Moderation,
            Version = (basis.Moderation?.Payload.Version ?? 0) + 1,
            Expires = now + ModerationLifetime,
            Entries = IndexContent.ModerationEntries(registry),
        };
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var path = Add(files, IndexPaths.Moderation, Sign(moderation, keys.Require(OnlineRole.Moderation)));
        notes.Add(string.Create(CultureInfo.InvariantCulture, $"Signed moderation list {moderation.Version} with {moderation.Entries.Count} active entries."));
        return basis.With(files, moderation: new SignedFile<ModerationPayload>(path, moderation));
    }

    private static IndexBuildResult Timestamped(IndexState state, OnlineKeys keys, DateTimeOffset now, List<string> notes)
    {
        if (keys.Find(OnlineRole.Timestamp) is not { } key || state.Snapshot is null || state.Moderation is null)
            return new IndexBuildResult(state, notes);

        var timestamp = new TimestampPayload
        {
            Type = PayloadTypes.Timestamp,
            Version = (state.Timestamp?.Version ?? 0) + 1,
            Expires = now + TimestampLifetime,
            Snapshot = FileMeta.Of(state.Snapshot.Payload.Version, state.Files[state.Snapshot.Path]),
            Moderation = FileMeta.Of(state.Moderation.Payload.Version, state.Files[state.Moderation.Path]),
        };
        notes.Add(string.Create(CultureInfo.InvariantCulture,
            $"Signed timestamp {timestamp.Version} for snapshot {timestamp.Snapshot.Version} and moderation list {timestamp.Moderation.Version}, expiring {timestamp.Expires:u}."));
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [IndexPaths.Timestamp] = Sign(timestamp, key) };
        return new IndexBuildResult(state.With(files, timestamp: timestamp), notes);
    }

    /// <summary>Keeps a document's version when its content is unchanged, and raises it by one otherwise.</summary>
    private static (FileMeta Meta, byte[] Bytes) Versioned<T>(T document, int? previousVersion, byte[]? previousBytes, Func<T, int, T> withVersion)
        where T : IndexDocument
    {
        if (previousVersion is { } version)
        {
            var same = IndexJson.Serialize(withVersion(document, version));
            if (previousBytes is not null && same.AsSpan().SequenceEqual(previousBytes))
                return (FileMeta.Of(version, same), same);
            var next = IndexJson.Serialize(withVersion(document, version + 1));
            return (FileMeta.Of(version + 1, next), next);
        }

        var first = IndexJson.Serialize(withVersion(document, 1));
        return (FileMeta.Of(1, first), first);
    }

    private static bool SamePins(SnapshotPayload snapshot, Dictionary<string, FileMeta> plugins, FileMeta publishers, FileMeta policy) =>
        snapshot.Publishers == publishers && snapshot.Policy == policy && snapshot.Plugins.Count == plugins.Count
        && plugins.All(pair => snapshot.Plugins.TryGetValue(pair.Key, out var meta) && meta == pair.Value);

    private static bool Renewable(DateTimeOffset expires, DateTimeOffset now) => expires - now <= RenewalWindow;

    private static IndexState Note(IndexState state, List<string> notes, string note)
    {
        notes.Add(note);
        return state;
    }

    private static string Add(Dictionary<string, byte[]> files, Func<string, string> pathFor, byte[] bytes)
    {
        var path = pathFor(Protocol.Hashes.Sha256(bytes));
        files[path] = bytes;
        return path;
    }

    private static byte[] Sign<T>(T payload, ISigner key)
        where T : SignedPayload => IndexJson.Serialize(Dsse.Sign(IndexJson.Serialize(payload), key));

    private DateTimeOffset Now() => UtcTimestampConverter.Truncate(time.GetUtcNow());
}
