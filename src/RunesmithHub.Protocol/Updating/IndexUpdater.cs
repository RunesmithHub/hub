using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Signing;

namespace RunesmithHub.Protocol.Updating;

/// <summary>
/// Brings the trusted view of the index up to date: follows the root chain, then checks the timestamp, snapshot, plugin records, publisher
/// list, policy and moderation list for signatures, hashes, lengths, versions and expiry, and saves everything at once.
/// </summary>
public sealed class IndexUpdater
{
    private readonly IIndexFetcher _fetcher;
    private readonly ITrustStore _store;
    private readonly ReadOnlyMemory<byte> _shippedRoot;
    private readonly RootPayload _shippedRootPayload;
    private readonly TimeProvider _time;

    /// <param name="shippedRoot">The root envelope shipped with the application, trusted without a previous root.</param>
    /// <exception cref="InvalidDataException">The shipped root can't be read or is not signed by its own root keys.</exception>
    public IndexUpdater(IIndexFetcher fetcher, ITrustStore store, ReadOnlyMemory<byte> shippedRoot, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        _fetcher = fetcher;
        _store = store;
        _shippedRoot = shippedRoot;
        _time = time;

        _shippedRootPayload = RootChain.VerifyFirst(shippedRoot.Span);
    }

    /// <summary>Loads the view saved by the last successful update, without contacting the index.</summary>
    public IndexView? LoadSavedView() => SavedView(_store.Load());

    /// <summary>Updates the trusted view from the index.</summary>
    public async Task<IndexUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
    {
        var saved = Start(_store.Load());
        try
        {
            var now = _time.GetUtcNow();
            var session = new Session(_fetcher, now, cancellationToken);
            var (rootBytes, root, keysChanged) = await session.UpdateRootAsync(saved.Root, saved.RootPayload).ConfigureAwait(false);
            if (root.Version != saved.RootPayload.Version || saved.FromShipped)
            {
                saved = saved with { Root = rootBytes, RootPayload = root, Files = keysChanged ? null : saved.Files, FromShipped = false };
                _store.Save(new TrustedIndex(rootBytes, saved.Files));
            }

            if (now >= root.Expires)
                throw new MetadataException(IndexUpdateFailureKind.RootExpired, $"Root version {root.Version} expired at {root.Expires:u}.", root.Expires);

            var previous = saved.Files is null ? null : PreviousState.Read(saved.Files);
            var (files, view) = await session.UpdateFilesAsync(root, previous).ConfigureAwait(false);
            var updated = new TrustedIndex(rootBytes, files);
            _store.Save(updated);
            return new IndexUpdateResult { View = view };
        }
        catch (IndexUnavailableException exception)
        {
            return Failed(new IndexUpdateFailure(IndexUpdateFailureKind.Offline, exception.Message));
        }
        catch (MetadataException exception)
        {
            return Failed(exception.ToFailure());
        }
        catch (InvalidDataException exception)
        {
            return Failed(new IndexUpdateFailure(IndexUpdateFailureKind.VerificationFailed, exception.Message));
        }
    }

    private IndexUpdateResult Failed(IndexUpdateFailure failure) => new() { View = LoadSavedView(), Failure = failure };

    private static IndexView? SavedView(TrustedIndex? saved)
    {
        if (saved?.Files is not { } files)
            return null;
        try
        {
            return IndexView.FromTrustedFiles(saved.Root, files);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private StartingPoint Start(TrustedIndex? saved)
    {
        if (saved is not null)
        {
            try
            {
                var payload = RootChain.Read(saved.Root.Span).Root;
                if (payload.Version >= _shippedRootPayload.Version)
                    return new StartingPoint(saved.Root, payload, saved.Files, FromShipped: false);
                var keysChanged = OnlineKeysChanged(payload, _shippedRootPayload);
                return new StartingPoint(_shippedRoot, _shippedRootPayload, keysChanged ? null : saved.Files, FromShipped: true);
            }
            catch (InvalidDataException)
            {
            }
        }

        return new StartingPoint(_shippedRoot, _shippedRootPayload, null, FromShipped: saved is not null);
    }

    private static bool OnlineKeysChanged(RootPayload before, RootPayload after) =>
        !before.Roles.Publish.HasSameKeys(after.Roles.Publish)
        || !before.Roles.Timestamp.HasSameKeys(after.Roles.Timestamp)
        || !before.Roles.Moderation.HasSameKeys(after.Roles.Moderation);

    private sealed record StartingPoint(ReadOnlyMemory<byte> Root, RootPayload RootPayload, TrustedFiles? Files, bool FromShipped);

    /// <summary>The payloads saved by the last update, for version and hash comparisons.</summary>
    private sealed record PreviousState(TrustedFiles Files, TimestampPayload Timestamp, SnapshotPayload Snapshot, ModerationPayload Moderation)
    {
        public static PreviousState? Read(TrustedFiles files)
        {
            try
            {
                return new PreviousState(
                    files,
                    Session.Payload<TimestampPayload>(IndexJson.Deserialize<DsseEnvelope>(files.Timestamp.Span), PayloadTypes.Timestamp),
                    Session.Payload<SnapshotPayload>(IndexJson.Deserialize<DsseEnvelope>(files.Snapshot.Span), PayloadTypes.Snapshot),
                    Session.Payload<ModerationPayload>(IndexJson.Deserialize<DsseEnvelope>(files.Moderation.Span), PayloadTypes.Moderation));
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }
    }

    private sealed class Session(IIndexFetcher fetcher, DateTimeOffset now, CancellationToken cancellationToken)
    {
        public async Task<(ReadOnlyMemory<byte> Bytes, RootPayload Root, bool KeysChanged)> UpdateRootAsync(ReadOnlyMemory<byte> bytes, RootPayload root)
        {
            var original = root;
            while (true)
            {
                var version = root.Version + 1;
                var path = IndexPaths.Root(version);
                var result = await fetcher.FetchAsync(path, IndexPaths.RootLimit, cancellationToken).ConfigureAwait(false);
                if (result.Status == FetchStatus.NotFound)
                    break;

                var next = CheckSize(result, path, IndexPaths.RootLimit);
                root = RootChain.VerifyNext(root, next.Span);
                bytes = next;
            }

            return (bytes, root, OnlineKeysChanged(original, root));
        }

        public async Task<(TrustedFiles Files, IndexView View)> UpdateFilesAsync(RootPayload root, PreviousState? previous)
        {
            var timestampBytes = await FetchTimestampAsync().ConfigureAwait(false);
            var timestampEnvelope = Envelope(timestampBytes, IndexPaths.Timestamp);
            var timestamp = Payload<TimestampPayload>(timestampEnvelope, PayloadTypes.Timestamp);
            Signed(timestampEnvelope, root, root.Roles.Timestamp, "timestamp");
            NotLower("timestamp", timestamp.Version, previous?.Timestamp.Version);
            NotExpired("timestamp", timestamp.Expires);

            var snapshotBytes = await FetchPinnedAsync(IndexPaths.Snapshot(timestamp.Snapshot.Sha256), timestamp.Snapshot, IndexPaths.SnapshotLimit, previous?.Files.Snapshot).ConfigureAwait(false);
            var snapshotEnvelope = Envelope(snapshotBytes, "snapshot");
            var snapshot = Payload<SnapshotPayload>(snapshotEnvelope, PayloadTypes.Snapshot);
            Signed(snapshotEnvelope, root, root.Roles.Publish, "snapshot");
            Matches("snapshot", snapshot.Version, timestamp.Snapshot.Version);
            NotLower("snapshot", snapshot.Version, previous?.Snapshot.Version);
            CheckRecordVersions(snapshot, previous?.Snapshot);
            NotExpired("snapshot", snapshot.Expires);

            var moderationBytes = await FetchPinnedAsync(IndexPaths.Moderation(timestamp.Moderation.Sha256), timestamp.Moderation, IndexPaths.ModerationLimit, previous?.Files.Moderation).ConfigureAwait(false);
            var moderationEnvelope = Envelope(moderationBytes, "moderation list");
            var moderation = Payload<ModerationPayload>(moderationEnvelope, PayloadTypes.Moderation);
            Signed(moderationEnvelope, root, root.Roles.Moderation, "moderation list");
            Matches("moderation list", moderation.Version, timestamp.Moderation.Version);
            NotLower("moderation list", moderation.Version, previous?.Moderation.Version);
            NotExpired("moderation list", moderation.Expires);

            var pluginBytes = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
            var plugins = new Dictionary<string, PluginRecord>(StringComparer.Ordinal);
            foreach (var (id, meta) in snapshot.Plugins.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                ReadOnlyMemory<byte>? known = previous is not null && previous.Files.Plugins.TryGetValue(id, out var existing) ? existing : null;
                var bytes = await FetchPinnedAsync(IndexPaths.Plugin(meta.Sha256), meta, IndexPaths.PluginRecordLimit, known).ConfigureAwait(false);
                var record = Document<PluginRecord>(bytes, PayloadTypes.Plugin, $"the record of {id}");
                if (!string.Equals(record.Id, id, StringComparison.Ordinal))
                    throw Failed($"The snapshot entry {id} pins the record of {record.Id}.");
                Matches($"the record of {id}", record.Version, meta.Version);
                pluginBytes[id] = bytes;
                plugins[id] = record;
            }

            var publishersBytes = await FetchPinnedAsync(IndexPaths.Publishers(snapshot.Publishers.Sha256), snapshot.Publishers, IndexPaths.PublishersLimit, previous?.Files.Publishers).ConfigureAwait(false);
            var publishers = Document<PublisherList>(publishersBytes, PayloadTypes.Publishers, "publisher list");
            Matches("publisher list", publishers.Version, snapshot.Publishers.Version);

            var policyBytes = await FetchPinnedAsync(IndexPaths.Policy(snapshot.Policy.Sha256), snapshot.Policy, IndexPaths.PolicyLimit, previous?.Files.Policy).ConfigureAwait(false);
            var policy = Document<PolicyPayload>(policyBytes, PayloadTypes.Policy, "policy file");
            Matches("policy file", policy.Version, snapshot.Policy.Version);

            var files = new TrustedFiles
            {
                Timestamp = timestampBytes,
                Snapshot = snapshotBytes,
                Moderation = moderationBytes,
                Publishers = publishersBytes,
                Policy = policyBytes,
                Plugins = pluginBytes,
            };
            return (files, new IndexView(root, timestamp, snapshot, moderation, plugins, publishers, policy));
        }

        private async Task<ReadOnlyMemory<byte>> FetchTimestampAsync()
        {
            var result = await fetcher.FetchAsync(IndexPaths.Timestamp, IndexPaths.TimestampLimit, cancellationToken).ConfigureAwait(false);
            if (result.Status == FetchStatus.NotFound)
                throw Failed("The index has no timestamp.");
            return CheckSize(result, IndexPaths.Timestamp, IndexPaths.TimestampLimit);
        }

        private async Task<ReadOnlyMemory<byte>> FetchPinnedAsync(string path, FileMeta meta, long limit, ReadOnlyMemory<byte>? known)
        {
            if (!Hashes.IsSha256(meta.Sha256))
                throw Failed($"{path} is pinned with a malformed hash.");
            if (meta.Length < 0 || meta.Length > limit)
                throw Failed($"{path} is pinned at {meta.Length} bytes, more than the {limit} allowed.");
            if (known is { } bytes && bytes.Length == meta.Length && Hashes.Matches(bytes.Span, meta.Sha256))
                return bytes;

            var result = await fetcher.FetchAsync(path, meta.Length, cancellationToken).ConfigureAwait(false);
            if (result.Status == FetchStatus.NotFound)
                throw Failed($"{path} is missing from the index.");
            var content = CheckSize(result, path, meta.Length);
            if (content.Length != meta.Length || !Hashes.Matches(content.Span, meta.Sha256))
                throw Failed($"{path} does not match the hash and length it is pinned with.");
            return content;
        }

        private static ReadOnlyMemory<byte> CheckSize(FetchResult result, string path, long limit)
        {
            if (result.Status == FetchStatus.TooLarge || result.Content.Length > limit)
                throw Failed($"{path} is larger than the {limit} bytes allowed.");
            return result.Content;
        }

        private static void CheckRecordVersions(SnapshotPayload snapshot, SnapshotPayload? previous)
        {
            if (previous is null)
                return;
            foreach (var (id, meta) in previous.Plugins)
            {
                if (snapshot.Plugins.TryGetValue(id, out var current) && current.Version < meta.Version)
                    throw Failed($"The record of {id} went from version {meta.Version} back to {current.Version}.");
            }

            NotLower("publisher list", snapshot.Publishers.Version, previous.Publishers.Version);
            NotLower("policy file", snapshot.Policy.Version, previous.Policy.Version);
        }

        private void NotExpired(string what, DateTimeOffset expires)
        {
            if (now >= expires)
                throw new MetadataException(IndexUpdateFailureKind.MetadataExpired, $"The {what} expired at {expires:u}.", expires);
        }

        private static void NotLower(string what, int version, int? previous)
        {
            if (version < previous)
                throw Failed($"The {what} went from version {previous} back to {version}.");
        }

        private static void Matches(string what, int version, int expected)
        {
            if (version != expected)
                throw Failed($"The {what} has version {version}, but it is pinned at version {expected}.");
        }

        private static void Signed(DsseEnvelope envelope, RootPayload root, RoleKeys role, string what)
        {
            if (!RoleVerifier.MeetsThreshold(envelope, root, role))
                throw Failed($"The {what} is not signed by enough of its role's keys.");
        }

        private static DsseEnvelope Envelope(ReadOnlyMemory<byte> bytes, string what)
        {
            try
            {
                return IndexJson.Deserialize<DsseEnvelope>(bytes.Span);
            }
            catch (InvalidDataException exception)
            {
                throw Failed($"The {what} is not a valid envelope: {exception.Message}");
            }
        }

        public static T Payload<T>(DsseEnvelope envelope, string expected)
            where T : SignedPayload
        {
            var payload = IndexJson.Deserialize<T>(envelope.Payload.Span);
            if (!string.Equals(payload.Type, expected, StringComparison.Ordinal))
                throw Failed($"Expected a {expected} payload but found {payload.Type}.");
            return payload;
        }

        private static T Document<T>(ReadOnlyMemory<byte> bytes, string type, string what)
            where T : IndexDocument
        {
            var document = IndexJson.Deserialize<T>(bytes.Span);
            if (!string.Equals(document.Type, type, StringComparison.Ordinal))
                throw Failed($"The {what} has type {document.Type}; expected {type}.");
            return document;
        }

        private static MetadataException Failed(string message) => new(IndexUpdateFailureKind.VerificationFailed, message);
    }
}
