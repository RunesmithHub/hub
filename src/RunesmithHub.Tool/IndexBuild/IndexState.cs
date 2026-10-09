using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Signing;

namespace RunesmithHub.Tool.IndexBuild;

/// <summary>A signed document of the index and the path of its envelope.</summary>
internal sealed record SignedFile<T>(string Path, T Payload)
    where T : SignedPayload;

/// <summary>
/// The files of an index directory, except the roots, with the timestamp, snapshot and moderation list that are current in it. The newest
/// snapshot and moderation list may be ahead of the timestamp until the timestamp is signed again; the files the timestamp names stay, so
/// clients can keep fetching them meanwhile.
/// </summary>
internal sealed class IndexState
{
    private static readonly string[] HashNamedFolders = ["snapshot/", "moderation/", "plugins/", "publishers/", "policy/"];

    private IndexState(
        IReadOnlyDictionary<string, byte[]> files, TimestampPayload? timestamp, SignedFile<SnapshotPayload>? snapshot, SignedFile<ModerationPayload>? moderation)
    {
        Files = files;
        Timestamp = timestamp;
        Snapshot = snapshot;
        Moderation = moderation;
    }

    /// <summary>Gets every file by its path relative to the index root.</summary>
    public IReadOnlyDictionary<string, byte[]> Files { get; }

    public TimestampPayload? Timestamp { get; }

    public SignedFile<SnapshotPayload>? Snapshot { get; }

    public SignedFile<ModerationPayload>? Moderation { get; }

    public static IndexState Empty { get; } = new(new Dictionary<string, byte[]>(StringComparer.Ordinal), null, null, null);

    public static IndexState Create(
        IReadOnlyDictionary<string, byte[]> files, TimestampPayload? timestamp, SignedFile<SnapshotPayload>? snapshot, SignedFile<ModerationPayload>? moderation) =>
        new(files, timestamp, snapshot, moderation);

    /// <summary>Gets the plugin record the snapshot pins for a plugin, with its bytes, or null.</summary>
    public (PluginRecord Record, byte[] Bytes)? PluginRecord(string id)
    {
        if (Snapshot is null || !Snapshot.Payload.Plugins.TryGetValue(id, out var meta) || !Files.TryGetValue(IndexPaths.Plugin(meta.Sha256), out var bytes))
            return null;
        return (IndexJson.Deserialize<PluginRecord>(bytes), bytes);
    }

    public byte[]? PublishersBytes => Snapshot is null ? null : Files.GetValueOrDefault(IndexPaths.Publishers(Snapshot.Payload.Publishers.Sha256));

    public byte[]? PolicyBytes => Snapshot is null ? null : Files.GetValueOrDefault(IndexPaths.Policy(Snapshot.Payload.Policy.Sha256));

    /// <summary>Gets the plugins, publishers and moderation entries of the current snapshot and moderation list.</summary>
    public HubCatalog Catalog()
    {
        var plugins = Snapshot?.Payload.Plugins.Keys.Select(PluginRecord).OfType<(PluginRecord Record, byte[] Bytes)>().Select(pair => pair.Record) ?? [];
        var publishers = PublishersBytes is { } bytes
            ? IndexJson.Deserialize<PublisherList>(bytes)
            : new PublisherList { Type = PayloadTypes.Publishers, Version = 0, Publishers = [] };
        return new HubCatalog(plugins, publishers, Moderation?.Payload.Entries ?? []);
    }

    /// <summary>Returns a state with more files and, for the parts given, new current documents.</summary>
    public IndexState With(
        IReadOnlyDictionary<string, byte[]> added, TimestampPayload? timestamp = null, SignedFile<SnapshotPayload>? snapshot = null,
        SignedFile<ModerationPayload>? moderation = null)
    {
        var files = new Dictionary<string, byte[]>(Files, StringComparer.Ordinal);
        foreach (var (path, bytes) in added)
            files[path] = bytes;
        return new IndexState(files, timestamp ?? Timestamp, snapshot ?? Snapshot, moderation ?? Moderation);
    }

    /// <summary>
    /// Reads an index directory, such as a checkout of the index branch. The newest snapshot and moderation list that verify against the
    /// trust root are current; files that neither they nor the timestamp need are dropped. Returns null when the folder holds no index.
    /// </summary>
    /// <exception cref="ToolException">A file is malformed, unsigned, missing or not named by its hash.</exception>
    public static IndexState? Read(string directory, TrustRoot trust)
    {
        ArgumentNullException.ThrowIfNull(trust);
        if (!Directory.Exists(directory))
            return null;

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in IndexOutput.ManagedFiles(directory).Where(path => !path.StartsWith("root/", StringComparison.Ordinal)))
            files[path] = File.ReadAllBytes(Path.Combine(directory, path));
        if (files.Count == 0)
            return null;

        foreach (var (path, bytes) in files.Where(pair => HashNamedFolders.Any(folder => pair.Key.StartsWith(folder, StringComparison.Ordinal))))
        {
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), Hashes.Sha256(bytes), StringComparison.Ordinal))
                throw new ToolException($"{path} is not named by its SHA-256.");
        }

        var snapshot = Newest<SnapshotPayload>(files, "snapshot/", PayloadTypes.Snapshot, trust, trust.Root.Roles.Publish);
        var moderation = Newest<ModerationPayload>(files, "moderation/", PayloadTypes.Moderation, trust, trust.Root.Roles.Moderation);
        TimestampPayload? timestamp = null;
        if (files.TryGetValue(IndexPaths.Timestamp, out var timestampBytes))
        {
            var (envelope, payload) = Read<TimestampPayload>(timestampBytes, PayloadTypes.Timestamp, IndexPaths.Timestamp);
            timestamp = trust.IsSignedBy(envelope, trust.Root.Roles.Timestamp) ? payload : throw new ToolException($"{IndexPaths.Timestamp} is not signed by the timestamp key.");
        }

        var keep = new HashSet<string>([IndexPaths.Timestamp, IndexPaths.Dashboard, IndexPaths.Stats], StringComparer.Ordinal);
        if (timestamp is not null)
            KeepGeneration(files, keep, IndexPaths.Snapshot(timestamp.Snapshot.Sha256), IndexPaths.Moderation(timestamp.Moderation.Sha256));
        KeepGeneration(files, keep, snapshot?.Path, moderation?.Path);
        var kept = files.Where(pair => keep.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return new IndexState(kept, timestamp, snapshot, moderation);
    }

    private static void KeepGeneration(Dictionary<string, byte[]> files, HashSet<string> keep, string? snapshotPath, string? moderationPath)
    {
        foreach (var path in new[] { snapshotPath, moderationPath }.OfType<string>())
        {
            if (!files.ContainsKey(path))
                throw new ToolException($"The index is incomplete: {path} is missing.");
            keep.Add(path);
        }

        if (snapshotPath is null)
            return;
        var snapshot = IndexJson.Deserialize<SnapshotPayload>(IndexJson.Deserialize<DsseEnvelope>(files[snapshotPath]).Payload.Span);
        var pinned = snapshot.Plugins.Values.Select(meta => IndexPaths.Plugin(meta.Sha256))
            .Append(IndexPaths.Publishers(snapshot.Publishers.Sha256))
            .Append(IndexPaths.Policy(snapshot.Policy.Sha256));
        foreach (var path in pinned)
        {
            if (!files.ContainsKey(path))
                throw new ToolException($"The index is incomplete: {path} is missing.");
            keep.Add(path);
        }
    }

    private static SignedFile<T>? Newest<T>(Dictionary<string, byte[]> files, string folder, string type, TrustRoot trust, RoleKeys role)
        where T : SignedPayload
    {
        SignedFile<T>? newest = null;
        foreach (var (path, bytes) in files.Where(pair => pair.Key.StartsWith(folder, StringComparison.Ordinal)))
        {
            var (envelope, payload) = Read<T>(bytes, type, path);
            if (trust.IsSignedBy(envelope, role) && (newest is null || payload.Version > newest.Payload.Version))
                newest = new SignedFile<T>(path, payload);
        }

        return newest;
    }

    private static (DsseEnvelope Envelope, T Payload) Read<T>(byte[] bytes, string type, string path)
        where T : SignedPayload
    {
        try
        {
            var envelope = IndexJson.Deserialize<DsseEnvelope>(bytes);
            var payload = IndexJson.Deserialize<T>(envelope.Payload.Span);
            return string.Equals(payload.Type, type, StringComparison.Ordinal) ? (envelope, payload) : throw new ToolException($"{path} is not a {type}.");
        }
        catch (InvalidDataException exception)
        {
            throw new ToolException($"{path}: {exception.Message}", exception);
        }
    }
}
