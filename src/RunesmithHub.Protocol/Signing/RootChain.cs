using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Protocol.Signing;

/// <summary>Verifies root envelopes: the first root against its own keys, and each later root against the one before it.</summary>
public static class RootChain
{
    /// <summary>Reads a root envelope's payload without checking any signature.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a root envelope.</exception>
    public static (DsseEnvelope Envelope, RootPayload Root) Read(ReadOnlySpan<byte> envelopeBytes)
    {
        var envelope = IndexJson.Deserialize<DsseEnvelope>(envelopeBytes);
        var root = IndexJson.Deserialize<RootPayload>(envelope.Payload.Span);
        if (!string.Equals(root.Type, PayloadTypes.Root, StringComparison.Ordinal))
            throw new InvalidDataException($"Expected a root payload but found {root.Type}.");
        return (envelope, root);
    }

    /// <summary>Verifies a root that is trusted on its own, such as the one shipped with Runesmith: it must meet its own root threshold.</summary>
    /// <exception cref="InvalidDataException">The root is malformed or not signed by enough of its own root keys.</exception>
    public static RootPayload VerifyFirst(ReadOnlySpan<byte> envelopeBytes)
    {
        var (envelope, root) = Read(envelopeBytes);
        if (!RoleVerifier.MeetsThreshold(envelope, root, root.Roles.Root))
            throw new InvalidDataException($"Root version {root.Version} is not signed by enough of its own root keys.");
        return root;
    }

    /// <summary>Verifies the root that follows a trusted one: exactly one version higher, signed by enough root keys of both.</summary>
    /// <exception cref="InvalidDataException">The root is malformed or a check fails; the message says which.</exception>
    public static RootPayload VerifyNext(RootPayload trusted, ReadOnlySpan<byte> envelopeBytes)
    {
        ArgumentNullException.ThrowIfNull(trusted);
        var (envelope, next) = Read(envelopeBytes);
        var expected = trusted.Version + 1;
        if (next.Version != expected)
            throw new InvalidDataException($"The root after version {trusted.Version} has version {next.Version}; expected {expected}.");
        if (!RoleVerifier.MeetsThreshold(envelope, trusted, trusted.Roles.Root))
            throw new InvalidDataException($"Root version {expected} is not signed by enough keys of root version {trusted.Version}.");
        if (!RoleVerifier.MeetsThreshold(envelope, next, next.Roles.Root))
            throw new InvalidDataException($"Root version {expected} is not signed by enough of its own root keys.");
        return next;
    }
}
