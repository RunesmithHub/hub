using System.Globalization;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Signing;

namespace RunesmithHub.Tool.Keys;

/// <summary>Creates, signs and verifies root envelopes for key ceremonies.</summary>
internal static class RootCeremony
{
    /// <summary>Creates an unsigned root envelope.</summary>
    /// <exception cref="ToolException">A role has no keys, or a threshold is not between 1 and its number of keys.</exception>
    public static byte[] Create(RootTemplate template)
    {
        if (template.Version < 1)
            throw new ToolException("The root version must be 1 or higher.");

        var root = new RootPayload
        {
            Type = PayloadTypes.Root,
            Version = template.Version,
            Expires = UtcTimestampConverter.Truncate(template.Expires),
            Keys = new[] { template.Root, template.Publish, template.Moderation, template.Timestamp }
                .SelectMany(role => role.Keys)
                .DistinctBy(key => key.KeyId)
                .ToDictionary(key => key.KeyId, key => key),
            Roles = new RootRoles
            {
                Root = Role("root", template.Root),
                Publish = Role("publish", template.Publish),
                Moderation = Role("moderation", template.Moderation),
                Timestamp = Role("timestamp", template.Timestamp),
            },
        };
        return IndexJson.Serialize(Dsse.Sign(IndexJson.Serialize(root)));
    }

    private static RoleKeys Role(string name, RoleTemplate template)
    {
        var keyIds = template.Keys.Select(key => key.KeyId).Distinct(StringComparer.Ordinal).ToList();
        if (keyIds.Count == 0)
            throw new ToolException($"The {name} role needs at least one key.");
        if (template.Threshold < 1 || template.Threshold > keyIds.Count)
            throw new ToolException($"The {name} threshold must be between 1 and {keyIds.Count}.");
        return new RoleKeys { KeyIds = keyIds, Threshold = template.Threshold };
    }

    /// <summary>Adds a signature to a root envelope, replacing an earlier one by the same key.</summary>
    public static (byte[] Envelope, RootPayload Root) Sign(ReadOnlySpan<byte> envelopeBytes, ISigner signer)
    {
        var (envelope, root) = RootChain.Read(envelopeBytes);
        return (IndexJson.Serialize(Dsse.AddSignature(envelope, signer)), root);
    }

    /// <summary>Verifies every root in a folder, <c>1.json</c> upward, as Runesmith would walk them.</summary>
    /// <returns>One line per root describing it; the last line says why the chain is invalid when it is.</returns>
    public static RootChainReport Verify(string directory, DateTimeOffset now)
    {
        var lines = new List<string>();
        RootPayload? trusted = null;
        for (var version = 1; ; version++)
        {
            var path = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{version}.json"));
            if (!File.Exists(path))
                break;

            try
            {
                var bytes = File.ReadAllBytes(path);
                trusted = trusted is null ? RootChain.VerifyFirst(bytes) : RootChain.VerifyNext(trusted, bytes);
                var envelope = RootChain.Read(bytes).Envelope;
                var signed = RoleVerifier.CountValidSignatures(envelope, trusted, trusted.Roles.Root);
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Root {trusted.Version}: valid, signed by {signed} of its {trusted.Roles.Root.KeyIds.Count} root keys, expires {trusted.Expires:yyyy-MM-dd}."));
            }
            catch (InvalidDataException exception)
            {
                lines.Add($"{Path.GetFileName(path)}: {exception.Message}");
                return new RootChainReport(lines, Valid: false);
            }
        }

        if (trusted is null)
        {
            lines.Add($"{directory} has no 1.json.");
            return new RootChainReport(lines, Valid: false);
        }

        if (now >= trusted.Expires)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Root {trusted.Version} expired at {trusted.Expires:yyyy-MM-dd HH:mm}Z."));
            return new RootChainReport(lines, Valid: false);
        }

        return new RootChainReport(lines, Valid: true);
    }
}

/// <summary>What verifying a root chain found.</summary>
internal sealed record RootChainReport(IReadOnlyList<string> Lines, bool Valid);
