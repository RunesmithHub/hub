using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Protocol.Signing;

/// <summary>Checks an envelope's signatures against a role of a root.</summary>
public static class RoleVerifier
{
    /// <summary>
    /// Counts the distinct key IDs listed for the role whose signature over the envelope verifies. A key counts only when the root's key
    /// ID for it matches the key.
    /// </summary>
    public static int CountValidSignatures(DsseEnvelope envelope, RootPayload root, RoleKeys role)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(role);
        if (!string.Equals(envelope.PayloadType, DsseEnvelope.HubPayloadType, StringComparison.Ordinal))
            return 0;

        var signed = envelope.SignedBytes();
        var verified = new HashSet<string>(StringComparer.Ordinal);
        foreach (var signature in envelope.Signatures)
        {
            if (verified.Contains(signature.KeyId) || !role.KeyIds.Contains(signature.KeyId, StringComparer.Ordinal))
                continue;
            if (!root.Keys.TryGetValue(signature.KeyId, out var key) || !string.Equals(key.KeyId, signature.KeyId, StringComparison.Ordinal))
                continue;
            if (key.Verify(signed, signature.Sig.Span))
                verified.Add(signature.KeyId);
        }

        return verified.Count;
    }

    /// <summary>Gets whether at least the role's threshold of distinct listed keys signed the envelope. A threshold below 1 never passes.</summary>
    public static bool MeetsThreshold(DsseEnvelope envelope, RootPayload root, RoleKeys role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return role.Threshold >= 1 && CountValidSignatures(envelope, root, role) >= role.Threshold;
    }
}
