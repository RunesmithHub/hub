using Microsoft.Extensions.Time.Testing;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Signing;
using RunesmithHub.Protocol.Tests.Support;
using RunesmithHub.Protocol.Updating;

namespace RunesmithHub.Protocol.Tests.Updating;

public sealed class IndexUpdaterTests : IDisposable
{
    private readonly TestHub _hub = new(Records.Time);
    private readonly FakeTimeProvider _clock = new(Records.Time.AddHours(1));
    private readonly InMemoryTrustStore _store = new();
    private readonly MemoryFetcher _fetcher;

    public IndexUpdaterTests()
    {
        _fetcher = new MemoryFetcher(_hub.Files);
        _hub.Publish();
    }

    public void Dispose() => _hub.Dispose();

    private IndexUpdater Updater() => new(_fetcher, _store, _hub.ShippedRoot, _clock);

    private async Task<IndexUpdateResult> UpdateAsync() => await Updater().UpdateAsync(TestContext.Current.CancellationToken);

    private async Task AssertFailsAsync(IndexUpdateFailureKind kind, string? messagePart = null)
    {
        var saves = _store.SaveCount;
        var before = _store.Load();

        var result = await UpdateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(kind, result.Failure.Kind);
        if (messagePart is not null)
            Assert.Contains(messagePart, result.Failure.Message, StringComparison.Ordinal);
        if (kind != IndexUpdateFailureKind.RootExpired)
        {
            Assert.Equal(saves, _store.SaveCount);
            Assert.Same(before, _store.Load());
        }
    }

    [Fact]
    public async Task VerifiesAndSavesAFreshIndex()
    {
        var result = await UpdateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(["lumen.todo", "runesmith.git"], result.View.Plugins.Keys.Order());
        Assert.Equal(1, result.View.Snapshot.Version);
        Assert.Equal(1, result.View.Root.Version);
        Assert.Equal(Categories.Default, result.View.Policy.Categories);
        Assert.Equal(_hub.Now.AddDays(3), result.View.ExpiresAt);
        Assert.NotNull(_store.Load()?.Files);
    }

    [Fact]
    public async Task ReusesUnchangedFilesOnTheNextUpdate()
    {
        await UpdateAsync();
        _fetcher.Requests.Clear();
        _hub.Plugins[0] = _hub.Plugins[0] with { Version = 2, Summary = "Changed." };
        _hub.Publish();

        var result = await UpdateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("Changed.", result.View.Plugins["lumen.todo"].Summary);
        Assert.Single(_fetcher.Requests, path => path.StartsWith("plugins/", StringComparison.Ordinal));
        Assert.DoesNotContain(_fetcher.Requests, path => path.StartsWith("publishers/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsOfflineWithTheSavedView()
    {
        await UpdateAsync();
        _fetcher.Offline = true;

        var result = await UpdateAsync();

        Assert.Equal(IndexUpdateFailureKind.Offline, result.Failure!.Kind);
        Assert.NotNull(result.View);
        Assert.Equal(1, result.View.Timestamp.Version);
    }

    [Fact]
    public async Task ReportsOfflineWithoutAViewBeforeTheFirstUpdate()
    {
        _fetcher.Offline = true;

        var result = await UpdateAsync();

        Assert.Equal(IndexUpdateFailureKind.Offline, result.Failure!.Kind);
        Assert.Null(result.View);
    }

    [Fact]
    public async Task RejectsATamperedPluginRecord()
    {
        var path = _hub.Files.Keys.First(key => key.StartsWith("plugins/", StringComparison.Ordinal));
        var bytes = _hub.Files[path];
        bytes[^2] ^= 1;

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "does not match the hash");
    }

    [Fact]
    public async Task RejectsARecordPinnedAtAnotherVersion()
    {
        var bytes = IndexJson.Serialize(_hub.Plugins[0]);
        var snapshot = IndexJson.Deserialize<SnapshotPayload>(IndexJson.Deserialize<DsseEnvelope>(_hub.Last.Snapshot).Payload.Span);
        var plugins = snapshot.Plugins.ToDictionary(pair => pair.Key, pair => pair.Value);
        plugins[_hub.Plugins[0].Id] = FileMeta.Of(5, bytes);
        var forged = TestHub.Envelope(snapshot with { Plugins = plugins }, _hub.PublishKey);
        _hub.Files[IndexPaths.Snapshot(Hashes.Sha256(forged))] = forged;
        _hub.Files[IndexPaths.Timestamp] = _hub.TimestampFor(forged, _hub.Last.Moderation, _hub.TimestampKey);

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "pinned at version 5");
    }

    [Fact]
    public async Task RejectsASnapshotServedAtAnotherHash()
    {
        var snapshotPath = _hub.Files.Keys.Single(key => key.StartsWith("snapshot/", StringComparison.Ordinal));
        var oldSnapshot = _hub.Files[snapshotPath];
        _hub.Publish();
        var newPath = IndexPaths.Snapshot(Hashes.Sha256(_hub.Last.Snapshot));
        _hub.Files[newPath] = oldSnapshot;

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "does not match the hash");
    }

    [Fact]
    public async Task RejectsAnOldTimestamp()
    {
        await UpdateAsync();
        var old = _hub.Last.Timestamp;
        _hub.Publish();
        await UpdateAsync();
        _hub.Files[IndexPaths.Timestamp] = old;

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "timestamp went from version 2 back to 1");
    }

    [Fact]
    public async Task RejectsASnapshotRollbackEvenWithTheTimestampKey()
    {
        var oldSnapshot = _hub.Last.Snapshot;
        _hub.Publish();
        await UpdateAsync();
        _hub.TimestampVersion++;
        _hub.Files[IndexPaths.Timestamp] = _hub.TimestampFor(oldSnapshot, _hub.Last.Moderation, _hub.TimestampKey);

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "snapshot went from version 2 back to 1");
    }

    [Fact]
    public async Task RejectsAModerationRollbackEvenWithTheTimestampKey()
    {
        var oldModeration = _hub.Last.Moderation;
        _hub.Moderation.Add(Records.Moderation(1, new ModerationTarget { Kind = ModerationTargetKind.Plugin, Plugin = "lumen.todo" }, ModerationAction.Block));
        _hub.Publish();
        await UpdateAsync();
        _hub.TimestampVersion++;
        _hub.Files[IndexPaths.Timestamp] = _hub.TimestampFor(_hub.Last.Snapshot, oldModeration, _hub.TimestampKey);

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "moderation list went from version 2 back to 1");
    }

    [Fact]
    public async Task RejectsAPluginRecordVersionGoingDown()
    {
        _hub.Plugins[0] = _hub.Plugins[0] with { Version = 3 };
        _hub.Publish();
        await UpdateAsync();
        _hub.Plugins[0] = _hub.Plugins[0] with { Version = 2 };
        _hub.Publish();

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "record of lumen.todo went from version 3 back to 2");
    }

    [Fact]
    public async Task RejectsPublisherAndPolicyVersionsGoingDown()
    {
        _hub.PublishersVersion = 3;
        _hub.Publish();
        await UpdateAsync();
        _hub.PublishersVersion = 2;
        _hub.Publish();

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "publisher list went from version 3 back to 2");
    }

    [Fact]
    public async Task ReportsAnExpiredTimestamp()
    {
        _clock.Advance(TimeSpan.FromDays(4));

        await AssertFailsAsync(IndexUpdateFailureKind.MetadataExpired, "timestamp expired");
    }

    [Fact]
    public async Task ReportsAnExpiredSnapshotAndModerationList()
    {
        _hub.SnapshotLifetime = TimeSpan.FromMinutes(30);
        _hub.Publish();
        await AssertFailsAsync(IndexUpdateFailureKind.MetadataExpired, "snapshot expired");

        _hub.SnapshotLifetime = TimeSpan.FromDays(90);
        _hub.ModerationLifetime = TimeSpan.FromMinutes(30);
        _hub.Publish();
        await AssertFailsAsync(IndexUpdateFailureKind.MetadataExpired, "moderation list expired");
    }

    [Fact]
    public async Task ReportsAnExpiredRoot()
    {
        _clock.Advance(TimeSpan.FromDays(366));

        var result = await UpdateAsync();

        Assert.Equal(IndexUpdateFailureKind.RootExpired, result.Failure!.Kind);
        Assert.Equal(_hub.Root.Expires, result.Failure.ExpiredAt);
    }

    [Fact]
    public async Task RejectsATimestampSignedByTheWrongKey()
    {
        _hub.Files[IndexPaths.Timestamp] = _hub.TimestampFor(_hub.Last.Snapshot, _hub.Last.Moderation, _hub.PublishKey);

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "timestamp is not signed");
    }

    [Fact]
    public async Task RejectsASnapshotSignedByTheWrongKey()
    {
        var publish = _hub.PublishKey;
        _hub.PublishKey = _hub.TimestampKey;
        _hub.Publish();
        _hub.PublishKey = publish;

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "snapshot is not signed");
    }

    [Fact]
    public async Task RejectsAModerationListSignedByAnUnknownKey()
    {
        _hub.ModerationKey = _hub.NewKey();
        _hub.Publish();

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "moderation list is not signed");
    }

    [Fact]
    public async Task RejectsAPayloadOfTheWrongTypeSignedByTheRightKey()
    {
        var snapshotAsTimestamp = TestHub.Envelope(
            IndexJson.Deserialize<SnapshotPayload>(IndexJson.Deserialize<DsseEnvelope>(_hub.Last.Snapshot).Payload.Span), _hub.TimestampKey);
        _hub.Files[IndexPaths.Timestamp] = snapshotAsTimestamp;

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed);
    }

    [Fact]
    public async Task RejectsARootSignedByTooFewOldRootKeys()
    {
        var next = TestHub.RootPayloadFor(2, _hub.RootKeys, _hub.NewKey(), _hub.ModerationKey, _hub.TimestampKey, _hub.Now.AddDays(365));
        _hub.Rotate(next, _hub.RootKeys[0]);

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "not signed by enough keys of root version 1");
    }

    [Fact]
    public async Task RejectsARootNotSignedByEnoughOfItsOwnKeys()
    {
        var oldKeys = _hub.RootKeys;
        EcdsaSigner[] newKeys = [_hub.NewKey(), _hub.NewKey(), _hub.NewKey()];
        var next = TestHub.RootPayloadFor(2, newKeys, _hub.PublishKey, _hub.ModerationKey, _hub.TimestampKey, _hub.Now.AddDays(365));
        _hub.Rotate(next, oldKeys[0], oldKeys[1], newKeys[0]);

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "enough of its own root keys");
    }

    [Fact]
    public async Task RejectsARootWithTheWrongVersion()
    {
        var next = TestHub.RootPayloadFor(3, _hub.RootKeys, _hub.PublishKey, _hub.ModerationKey, _hub.TimestampKey, _hub.Now.AddDays(365));
        _hub.Files[IndexPaths.Root(2)] = TestHub.Envelope(next, _hub.RootKeys[0], _hub.RootKeys[1]);

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "expected 2");
    }

    [Fact]
    public async Task FollowsARootChainOverSeveralVersions()
    {
        _hub.RotateRootKeys();
        _hub.RotateOnlineKeys();
        _hub.RotateRootKeys();
        _hub.RotateOnlineKeys(publish: false, timestamp: false, moderation: true);
        _hub.Publish();

        var result = await UpdateAsync();

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.Equal(5, result.View.Root.Version);
        Assert.Contains(_hub.ModerationKey.KeyId, result.View.Root.Roles.Moderation.KeyIds);
        Assert.Contains("root/6.json", _fetcher.Requests);
    }

    [Fact]
    public async Task RecoversFromAFastForwardAttackAfterTheKeysAreRotated()
    {
        await UpdateAsync();
        _hub.SnapshotVersion = 999;
        _hub.TimestampVersion = 999;
        _hub.Plugins[0] = _hub.Plugins[0] with { Version = 1000 };
        _hub.Publish();
        Assert.True((await UpdateAsync()).Succeeded);

        _hub.SnapshotVersion = 1;
        _hub.TimestampVersion = 1;
        _hub.Plugins[0] = _hub.Plugins[0] with { Version = 2 };
        _hub.Publish();
        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "back to");

        _hub.RotateOnlineKeys();
        _hub.Write();
        var result = await UpdateAsync();

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.Equal(2, result.View.Snapshot.Version);
        Assert.Equal(2, result.View.Plugins["lumen.todo"].Version);
    }

    [Fact]
    public async Task KeepsVersionMemoryWhenARootRotationLeavesTheOnlineKeysAlone()
    {
        _hub.Publish();
        await UpdateAsync();
        _hub.RotateRootKeys();
        _hub.Publish();
        _hub.Files[IndexPaths.Timestamp] = _hub.TimestampFor(_hub.Last.Snapshot, _hub.Last.Moderation, _hub.TimestampKey, version: 1);

        var result = await UpdateAsync();

        Assert.Equal(IndexUpdateFailureKind.VerificationFailed, result.Failure!.Kind);
        Assert.Equal(2, IndexJson.Deserialize<RootPayload>(IndexJson.Deserialize<DsseEnvelope>(_store.Load()!.Root.Span).Payload.Span).Version);
    }

    [Fact]
    public async Task RejectsAnOversizedTimestamp()
    {
        _hub.Files[IndexPaths.Timestamp] = new byte[IndexPaths.TimestampLimit + 1];

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "larger than");
    }

    [Fact]
    public async Task RejectsAnOversizedRoot()
    {
        _hub.Files[IndexPaths.Root(2)] = new byte[IndexPaths.RootLimit + 1];

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "larger than");
    }

    [Fact]
    public async Task RejectsAFilePinnedAboveItsSizeLimit()
    {
        var snapshot = IndexJson.Deserialize<SnapshotPayload>(IndexJson.Deserialize<DsseEnvelope>(_hub.Last.Snapshot).Payload.Span);
        var plugins = snapshot.Plugins.ToDictionary(pair => pair.Key, pair => pair.Value);
        plugins["lumen.todo"] = plugins["lumen.todo"] with { Length = IndexPaths.PluginRecordLimit + 1 };
        var forged = TestHub.Envelope(snapshot with { Plugins = plugins }, _hub.PublishKey);
        _hub.Files[IndexPaths.Snapshot(Hashes.Sha256(forged))] = forged;
        _hub.Files[IndexPaths.Timestamp] = _hub.TimestampFor(forged, _hub.Last.Moderation, _hub.TimestampKey);

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "more than the");
    }

    [Fact]
    public async Task RejectsAFileLongerThanItIsPinnedAt()
    {
        var path = _hub.Files.Keys.First(key => key.StartsWith("plugins/", StringComparison.Ordinal));
        _hub.Files[path] = [.. _hub.Files[path], (byte)' '];

        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "larger than");
    }

    [Fact]
    public async Task RejectsAMissingTimestampOrPinnedFile()
    {
        var policy = _hub.Files.Keys.First(key => key.StartsWith("policy/", StringComparison.Ordinal));
        _hub.Files.Remove(policy);
        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "missing");

        _hub.Files.Remove(IndexPaths.Timestamp);
        await AssertFailsAsync(IndexUpdateFailureKind.VerificationFailed, "no timestamp");
    }

    [Fact]
    public async Task UsesANewerShippedRootOverAnOlderSavedOne()
    {
        await UpdateAsync();
        _hub.RotateOnlineKeys();
        _hub.Write();
        var shipped = _hub.Files[IndexPaths.Root(2)];

        var result = await new IndexUpdater(_fetcher, _store, shipped, _clock).UpdateAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.Equal(2, result.View.Root.Version);
    }

    [Fact]
    public void RefusesAShippedRootNotSignedByItsOwnKeys()
    {
        var unsigned = TestHub.Envelope(_hub.Root, _hub.RootKeys[0]);

        Assert.Throws<InvalidDataException>(() => new IndexUpdater(_fetcher, _store, unsigned, _clock));
    }

    [Fact]
    public async Task LoadsTheSavedViewWithoutTheNetwork()
    {
        await UpdateAsync();
        _fetcher.Offline = true;

        var view = Updater().LoadSavedView();

        Assert.NotNull(view);
        Assert.Equal(2, view.Plugins.Count);
        Assert.False(view.IsExpired(_clock.GetUtcNow()));
        Assert.True(view.IsExpired(_clock.GetUtcNow().AddDays(3)));
    }

    [Fact]
    public async Task WorksWithTheFileTrustStore()
    {
        using var folder = new TemporaryFolder();
        var store = new FileTrustStore(folder.Path);
        var updater = new IndexUpdater(_fetcher, store, _hub.ShippedRoot, _clock);

        Assert.True((await updater.UpdateAsync(TestContext.Current.CancellationToken)).Succeeded);
        _hub.Plugins[1] = _hub.Plugins[1] with { Version = 2, Summary = "New." };
        _hub.Publish();
        var result = await new IndexUpdater(_fetcher, new FileTrustStore(folder.Path), _hub.ShippedRoot, _clock).UpdateAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.Equal("New.", result.View.Plugins["runesmith.git"].Summary);
    }
}
