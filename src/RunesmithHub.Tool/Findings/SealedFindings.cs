using RunesmithHub.Tool.Json;

namespace RunesmithHub.Tool.Findings;

/// <summary>
/// <c>private-findings.sealed.json</c>: findings encrypted to one administrator key. An ephemeral P-256 key agrees a secret with the
/// recipient's key (ECDH); HKDF-SHA256 turns it into an AES-256-GCM key, with the ephemeral key's SubjectPublicKeyInfo as the salt and
/// <see cref="FormatName"/> followed by the recipient's SubjectPublicKeyInfo as the info. The format and recipient key ID are the
/// additional authenticated data.
/// </summary>
/// <param name="Recipient">The recipient's key ID.</param>
/// <param name="EphemeralKey">The ephemeral key's SubjectPublicKeyInfo DER bytes.</param>
internal sealed record SealedFindings(string Format, string Recipient, byte[] EphemeralKey, byte[] Nonce, byte[] Ciphertext, byte[] Tag)
{
    public const string FileName = "private-findings.sealed.json";

    public const string FormatName = "rshub-sealed-findings-1";

    /// <exception cref="ToolException">The file is not sealed findings this tool can read.</exception>
    public static SealedFindings Read(ReadOnlySpan<byte> json)
    {
        SealedFindings sealedFindings;
        try
        {
            sealedFindings = ArtifactJson.Deserialize<SealedFindings>(json);
        }
        catch (InvalidDataException exception)
        {
            throw new ToolException($"Not sealed findings: {exception.Message}", exception);
        }

        return sealedFindings.Format == FormatName ? sealedFindings : throw new ToolException($"The findings are in the format \"{sealedFindings.Format}\"; this tool reads {FormatName}.");
    }
}
