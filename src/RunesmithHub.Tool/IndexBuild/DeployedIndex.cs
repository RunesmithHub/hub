using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Updating;

namespace RunesmithHub.Tool.IndexBuild;

/// <summary>The index currently served, verified from the trust root exactly as clients verify it.</summary>
internal static class DeployedIndex
{
    private const long InformationalLimit = 8 * 1024 * 1024;

    /// <summary>
    /// Downloads and verifies the deployed index, or returns null when nothing is deployed yet. Expired metadata is accepted, because
    /// re-signing it is what the signing jobs are for; every signature, hash and version is still checked.
    /// </summary>
    /// <exception cref="ToolException">The deployed index does not verify.</exception>
    public static async Task<IndexState?> LoadAsync(IIndexFetcher fetcher, TrustRoot trust, TimeProvider time, CancellationToken cancellationToken)
    {
        if ((await fetcher.FetchAsync(IndexPaths.Timestamp, IndexPaths.TimestampLimit, cancellationToken).ConfigureAwait(false)).Status == FetchStatus.NotFound)
            return null;

        var clock = time;
        for (var attempt = 0; ; attempt++)
        {
            var store = new InMemoryTrustStore();
            var result = await new IndexUpdater(fetcher, store, trust.Envelopes[0], clock).UpdateAsync(cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
                return await FromFilesAsync(store.Load()!.Files!, result.View, fetcher, cancellationToken).ConfigureAwait(false);

            if (result.Failure is { Kind: IndexUpdateFailureKind.MetadataExpired, ExpiredAt: { } expiredAt } && attempt < 4)
            {
                clock = new FixedTimeProvider(expiredAt.AddSeconds(-1));
                continue;
            }

            throw new ToolException($"The deployed index does not verify: {result.Failure!.Message}");
        }
    }

    private static async Task<IndexState> FromFilesAsync(TrustedFiles files, IndexView view, IIndexFetcher fetcher, CancellationToken cancellationToken)
    {
        var snapshotPath = IndexPaths.Snapshot(view.Timestamp.Snapshot.Sha256);
        var moderationPath = IndexPaths.Moderation(view.Timestamp.Moderation.Sha256);
        var all = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [IndexPaths.Timestamp] = files.Timestamp.ToArray(),
            [snapshotPath] = files.Snapshot.ToArray(),
            [moderationPath] = files.Moderation.ToArray(),
            [IndexPaths.Publishers(view.Snapshot.Publishers.Sha256)] = files.Publishers.ToArray(),
            [IndexPaths.Policy(view.Snapshot.Policy.Sha256)] = files.Policy.ToArray(),
        };
        foreach (var (id, meta) in view.Snapshot.Plugins)
            all[IndexPaths.Plugin(meta.Sha256)] = files.Plugins[id].ToArray();

        foreach (var informational in new[] { IndexPaths.Dashboard, IndexPaths.Stats })
        {
            var fetched = await fetcher.FetchAsync(informational, InformationalLimit, cancellationToken).ConfigureAwait(false);
            if (fetched.Status == FetchStatus.Found)
                all[informational] = fetched.Content.ToArray();
        }

        return IndexState.Create(all, view.Timestamp, new SignedFile<SnapshotPayload>(snapshotPath, view.Snapshot), new SignedFile<ModerationPayload>(moderationPath, view.Moderation));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
