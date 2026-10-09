using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Signing;

namespace RunesmithHub.Protocol.Tests.Support;

/// <summary>A hub index in memory, with its keys, that tests publish to and then tamper with.</summary>
internal sealed class TestHub : IDisposable
{
    private readonly List<EcdsaSigner> _keys = [];

    public TestHub(DateTimeOffset now)
    {
        Now = now;
        RootKeys = [NewKey(), NewKey(), NewKey()];
        PublishKey = NewKey();
        ModerationKey = NewKey();
        TimestampKey = NewKey();
        Root = RootPayloadFor(1, RootKeys, PublishKey, ModerationKey, TimestampKey, now.AddDays(365));
        Files[IndexPaths.Root(1)] = Envelope(Root, RootKeys[0], RootKeys[1]);
        Plugins.Add(Records.Plugin("lumen.todo", "Lumen Todo", PluginTier.Unverified, [Records.Version("1.0.0", review: ReviewState.Automated)]));
        Plugins.Add(Records.Plugin("runesmith.git", "Git", PluginTier.Official, [Records.Version("0.3.4")]));
    }

    public DateTimeOffset Now { get; set; }

    /// <summary>Gets the files the host serves, by path.</summary>
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public byte[] ShippedRoot => Files[IndexPaths.Root(1)];

    public EcdsaSigner[] RootKeys { get; private set; }

    public EcdsaSigner PublishKey { get; set; }

    public EcdsaSigner ModerationKey { get; set; }

    public EcdsaSigner TimestampKey { get; set; }

    public RootPayload Root { get; private set; }

    public List<PluginRecord> Plugins { get; } = [];

    public List<ModerationEntry> Moderation { get; } = [];

    public int TimestampVersion { get; set; }

    public int SnapshotVersion { get; set; }

    public int ModerationVersion { get; set; }

    public int PublishersVersion { get; set; } = 1;

    public int PolicyVersion { get; set; } = 1;

    public TimeSpan TimestampLifetime { get; set; } = TimeSpan.FromDays(3);

    public TimeSpan SnapshotLifetime { get; set; } = TimeSpan.FromDays(90);

    public TimeSpan ModerationLifetime { get; set; } = TimeSpan.FromDays(90);

    /// <summary>Gets the bytes of the last snapshot, moderation list and timestamp written.</summary>
    public (byte[] Snapshot, byte[] Moderation, byte[] Timestamp) Last { get; private set; }

    public EcdsaSigner NewKey()
    {
        var key = EcdsaSigner.Generate();
        _keys.Add(key);
        return key;
    }

    public static RootPayload RootPayloadFor(int version, EcdsaSigner[] rootKeys, EcdsaSigner publish, EcdsaSigner moderation, EcdsaSigner timestamp, DateTimeOffset expires)
    {
        var all = rootKeys.Concat([publish, moderation, timestamp]).DistinctBy(key => key.KeyId);
        return new RootPayload
        {
            Type = PayloadTypes.Root,
            Version = version,
            Expires = expires,
            Keys = all.ToDictionary(key => key.KeyId, key => key.PublicKey),
            Roles = new RootRoles
            {
                Root = new RoleKeys { KeyIds = [.. rootKeys.Select(key => key.KeyId)], Threshold = 2 },
                Publish = new RoleKeys { KeyIds = [publish.KeyId], Threshold = 1 },
                Moderation = new RoleKeys { KeyIds = [moderation.KeyId], Threshold = 1 },
                Timestamp = new RoleKeys { KeyIds = [timestamp.KeyId], Threshold = 1 },
            },
        };
    }

    public static byte[] Envelope<T>(T payload, params ISigner[] signers)
        where T : class => IndexJson.Serialize(Dsse.Sign(IndexJson.Serialize(payload), signers));

    /// <summary>Writes the next root, signed by the given keys, and makes it current.</summary>
    public void Rotate(RootPayload next, params ISigner[] signers)
    {
        Files[IndexPaths.Root(next.Version)] = Envelope(next, signers);
        Root = next;
    }

    /// <summary>Replaces the online keys with new ones in a new root signed by two old and two new root keys.</summary>
    public void RotateOnlineKeys(bool publish = true, bool timestamp = true, bool moderation = false)
    {
        PublishKey = publish ? NewKey() : PublishKey;
        TimestampKey = timestamp ? NewKey() : TimestampKey;
        ModerationKey = moderation ? NewKey() : ModerationKey;
        var next = RootPayloadFor(Root.Version + 1, RootKeys, PublishKey, ModerationKey, TimestampKey, Now.AddDays(365));
        Rotate(next, RootKeys[0], RootKeys[1]);
    }

    /// <summary>Replaces the root keys with three new ones in a new root signed by two old and two new root keys.</summary>
    public void RotateRootKeys()
    {
        var old = RootKeys;
        RootKeys = [NewKey(), NewKey(), NewKey()];
        var next = RootPayloadFor(Root.Version + 1, RootKeys, PublishKey, ModerationKey, TimestampKey, Now.AddDays(365));
        Rotate(next, old[0], old[2], RootKeys[1], RootKeys[2]);
    }

    /// <summary>Publishes the current plugins and moderation entries with new snapshot, moderation and timestamp versions.</summary>
    public void Publish()
    {
        SnapshotVersion++;
        ModerationVersion++;
        TimestampVersion++;
        Write();
    }

    /// <summary>Writes every file for the current versions and contents.</summary>
    public void Write()
    {
        var plugins = new Dictionary<string, FileMeta>(StringComparer.Ordinal);
        foreach (var plugin in Plugins)
        {
            var bytes = IndexJson.Serialize(plugin);
            plugins[plugin.Id] = FileMeta.Of(plugin.Version, bytes);
            Files[IndexPaths.Plugin(Hashes.Sha256(bytes))] = bytes;
        }

        var publishers = IndexJson.Serialize(Records.Publishers(Plugins) with { Version = PublishersVersion });
        Files[IndexPaths.Publishers(Hashes.Sha256(publishers))] = publishers;
        var policy = IndexJson.Serialize(new PolicyPayload
        {
            Type = PayloadTypes.Policy,
            Version = PolicyVersion,
            Categories = Categories.Default,
            Capabilities = Capabilities.DefaultLabels.ToDictionary(pair => pair.Key, pair => new CapabilityLabel { Label = pair.Value }),
            OldestRunesmith = null,
            Mirrors = [],
            HoldHours = 24,
        });
        Files[IndexPaths.Policy(Hashes.Sha256(policy))] = policy;

        var snapshot = Envelope(new SnapshotPayload
        {
            Type = PayloadTypes.Snapshot,
            Version = SnapshotVersion,
            Expires = Now + SnapshotLifetime,
            Plugins = plugins,
            Publishers = FileMeta.Of(PublishersVersion, publishers),
            Policy = FileMeta.Of(PolicyVersion, policy),
        }, PublishKey);
        Files[IndexPaths.Snapshot(Hashes.Sha256(snapshot))] = snapshot;

        var moderation = Envelope(new ModerationPayload
        {
            Type = PayloadTypes.Moderation,
            Version = ModerationVersion,
            Expires = Now + ModerationLifetime,
            Entries = [.. Moderation],
        }, ModerationKey);
        Files[IndexPaths.Moderation(Hashes.Sha256(moderation))] = moderation;

        var timestamp = TimestampFor(snapshot, moderation, TimestampKey);
        Files[IndexPaths.Timestamp] = timestamp;
        Last = (snapshot, moderation, timestamp);
    }

    /// <summary>Signs a timestamp naming the given snapshot and moderation list, with the next timestamp version.</summary>
    public byte[] TimestampFor(byte[] snapshot, byte[] moderation, ISigner signer, int? version = null) => Envelope(new TimestampPayload
    {
        Type = PayloadTypes.Timestamp,
        Version = version ?? TimestampVersion,
        Expires = Now + TimestampLifetime,
        Snapshot = FileMeta.Of(VersionOf<SnapshotPayload>(snapshot), snapshot),
        Moderation = FileMeta.Of(VersionOf<ModerationPayload>(moderation), moderation),
    }, signer);

    private static int VersionOf<T>(byte[] envelope)
        where T : SignedPayload => IndexJson.Deserialize<T>(IndexJson.Deserialize<DsseEnvelope>(envelope).Payload.Span).Version;

    public void Dispose()
    {
        foreach (var key in _keys)
            key.Dispose();
    }
}
