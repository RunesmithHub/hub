using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Signing;

/// <summary>One signature in a DSSE envelope.</summary>
public sealed record DsseSignature
{
    [JsonPropertyName("keyid")]
    public required string KeyId { get; init; }

    /// <summary>Gets the IEEE P1363 signature; base64 in JSON.</summary>
    public required ReadOnlyMemory<byte> Sig { get; init; }
}
