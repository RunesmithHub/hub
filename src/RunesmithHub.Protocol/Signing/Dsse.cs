using System.Globalization;
using System.Text;

namespace RunesmithHub.Protocol.Signing;

/// <summary>Creates and signs DSSE envelopes.</summary>
public static class Dsse
{
    /// <summary>
    /// Returns <c>"DSSEv1" SP LEN(type) SP type SP LEN(body) SP body</c>, with lengths as ASCII decimal byte counts.
    /// </summary>
    public static byte[] PreAuthenticationEncoding(string payloadType, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(payloadType);
        var type = Encoding.UTF8.GetBytes(payloadType);
        var header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"DSSEv1 {type.Length} "));
        var middle = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $" {payload.Length} "));
        return [.. header, .. type, .. middle, .. payload];
    }

    /// <summary>Creates a hub envelope over the payload, signed by each signer.</summary>
    public static DsseEnvelope Sign(ReadOnlyMemory<byte> payload, params IEnumerable<ISigner> signers)
    {
        var envelope = new DsseEnvelope { PayloadType = DsseEnvelope.HubPayloadType, Payload = payload, Signatures = [] };
        return signers.Aggregate(envelope, AddSignature);
    }

    /// <summary>Returns the envelope with the signer's signature added, replacing an earlier signature by the same key.</summary>
    public static DsseEnvelope AddSignature(DsseEnvelope envelope, ISigner signer)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(signer);
        var signature = new DsseSignature { KeyId = signer.KeyId, Sig = signer.Sign(envelope.SignedBytes()) };
        return envelope with
        {
            Signatures = [.. envelope.Signatures.Where(existing => !string.Equals(existing.KeyId, signer.KeyId, StringComparison.Ordinal)), signature],
        };
    }
}
