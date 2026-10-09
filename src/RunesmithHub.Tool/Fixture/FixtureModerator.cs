using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Signing;
using RunesmithHub.Tool.Index;
using RunesmithHub.Tool.Keys;

namespace RunesmithHub.Tool.Fixture;

/// <summary>What a moderation change to a fixture produced.</summary>
internal sealed record FixtureModeration(ModerationEntry Entry, int ModerationVersion, int TimestampVersion);

/// <summary>
/// Adds a freeze or block to the moderation list of an index that <see cref="FixtureWriter"/> wrote, and signs the new list and a new
/// timestamp with the fixture's test keys, so clients can see a moderation action arrive in a running fixture.
/// </summary>
internal sealed class FixtureModerator(TimeProvider time)
{
    private const int ModerationDays = 90;
    private const int TimestampDays = 3;

    /// <exception cref="ToolException">The folder is not a fixture with its test keys.</exception>
    public FixtureModeration Moderate(string directory, ModerationAction action, ModerationTarget target, ModerationReason reason, string note)
    {
        directory = Path.GetFullPath(directory);
        var index = new IndexFolder(directory);
        var now = UtcTimestampConverter.Truncate(time.GetUtcNow());
        var timestamp = ReadPayload<TimestampPayload>(Read(directory, IndexPaths.Timestamp));
        var moderation = ReadPayload<ModerationPayload>(Read(directory, IndexPaths.Moderation(timestamp.Moderation.Sha256)));
        using var moderationKey = Key(directory, "moderation");
        using var timestampKey = Key(directory, "timestamp");

        var entry = new ModerationEntry
        {
            Id = moderation.Entries.Select(existing => existing.Id).DefaultIfEmpty(0).Max() + 1,
            Target = target,
            Action = action,
            Reason = reason,
            Note = note,
            CreatedAt = now,
        };
        var updated = moderation with { Version = moderation.Version + 1, Expires = now.AddDays(ModerationDays), Entries = [.. moderation.Entries, entry] };
        var moderationMeta = index.WritePinned(IndexPaths.Moderation, Sign(updated, moderationKey), updated.Version);
        var renewed = timestamp with { Version = timestamp.Version + 1, Expires = now.AddDays(TimestampDays), Moderation = moderationMeta };
        index.Write(IndexPaths.Timestamp, Sign(renewed, timestampKey));
        return new FixtureModeration(entry, updated.Version, renewed.Version);
    }

    private static byte[] Read(string directory, string relativePath)
    {
        var path = Path.Combine(directory, relativePath);
        return File.Exists(path) ? File.ReadAllBytes(path) : throw new ToolException($"{directory} is not a fixture: it has no {relativePath}.");
    }

    private static T ReadPayload<T>(byte[] envelope)
        where T : SignedPayload => IndexJson.Deserialize<T>(IndexJson.Deserialize<DsseEnvelope>(envelope).Payload.Span);

    private static EcdsaSigner Key(string directory, string role)
    {
        var path = Path.Combine(directory, FixtureKeys.Folder, $"test-{role}.pem");
        return File.Exists(path)
            ? KeyFiles.ReadSigner(path, () => throw new ToolException($"{path} is encrypted; fixture keys never are."))
            : throw new ToolException($"{directory} has no {FixtureKeys.Folder}/test-{role}.pem to sign with.");
    }

    private static byte[] Sign<T>(T payload, EcdsaSigner key)
        where T : SignedPayload => IndexJson.Serialize(Dsse.Sign(IndexJson.Serialize(payload), key));
}
