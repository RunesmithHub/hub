namespace RunesmithHub.Protocol.Signing;

/// <summary>A DSSE envelope: the exact payload bytes and the signatures over them.</summary>
public sealed record DsseEnvelope
{
    /// <summary>The payload type of every hub envelope.</summary>
    public const string HubPayloadType = "application/vnd.runesmith.hub+json";

    public required string PayloadType { get; init; }

    /// <summary>Gets the payload bytes; base64 in JSON.</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    public required IReadOnlyList<DsseSignature> Signatures { get; init; }

    /// <summary>Gets the bytes the signatures cover: the DSSE pre-authentication encoding of the payload.</summary>
    public byte[] SignedBytes() => Dsse.PreAuthenticationEncoding(PayloadType, Payload.Span);
}
