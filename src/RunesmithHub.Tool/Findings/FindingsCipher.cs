using System.Security.Cryptography;
using System.Text;
using RunesmithHub.Protocol.Signing;

namespace RunesmithHub.Tool.Findings;

/// <summary>Seals findings to an administrator's public key and opens them with the private key, as <see cref="SealedFindings"/> describes.</summary>
internal static class FindingsCipher
{
    /// <summary>The environment variable with the public key, in the key file format, that findings are sealed to.</summary>
    public const string RecipientVariable = "HUB_FINDINGS_RECIPIENT";

    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    public static SealedFindings Seal(ReadOnlySpan<byte> plaintext, PublicKey recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        using var recipientKey = ECDiffieHellman.Create();
        recipientKey.ImportSubjectPublicKeyInfo(recipient.SubjectPublicKeyInfo.Span, out _);
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var ephemeralKey = ephemeral.ExportSubjectPublicKeyInfo();
        var key = DeriveKey(ephemeral.DeriveRawSecretAgreement(recipientKey.PublicKey), ephemeralKey, recipient.SubjectPublicKeyInfo.Span);

        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];
        using (var aes = new AesGcm(key, TagBytes))
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(recipient.KeyId));
        return new SealedFindings(SealedFindings.FormatName, recipient.KeyId, ephemeralKey, nonce, ciphertext, tag);
    }

    /// <exception cref="ToolException">The findings are sealed to another key, or were changed.</exception>
    public static byte[] Open(SealedFindings sealedFindings, ECDiffieHellman privateKey)
    {
        ArgumentNullException.ThrowIfNull(sealedFindings);
        ArgumentNullException.ThrowIfNull(privateKey);
        var recipient = privateKey.ExportSubjectPublicKeyInfo();
        if (!string.Equals(Protocol.Hashes.Sha256(recipient), sealedFindings.Recipient, StringComparison.Ordinal))
            throw new ToolException($"The findings are sealed to the key {sealedFindings.Recipient}, not this one.");

        try
        {
            using var ephemeral = ECDiffieHellman.Create();
            ephemeral.ImportSubjectPublicKeyInfo(sealedFindings.EphemeralKey, out _);
            var key = DeriveKey(privateKey.DeriveRawSecretAgreement(ephemeral.PublicKey), sealedFindings.EphemeralKey, recipient);
            var plaintext = new byte[sealedFindings.Ciphertext.Length];
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(sealedFindings.Nonce, sealedFindings.Ciphertext, sealedFindings.Tag, plaintext, AssociatedData(sealedFindings.Recipient));
            return plaintext;
        }
        catch (CryptographicException exception)
        {
            throw new ToolException("The findings can't be opened: they were changed or are damaged.", exception);
        }
    }

    private static byte[] DeriveKey(byte[] sharedSecret, ReadOnlySpan<byte> ephemeralKey, ReadOnlySpan<byte> recipientKey)
    {
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, KeyBytes, ephemeralKey.ToArray(), [.. Encoding.ASCII.GetBytes(SealedFindings.FormatName), .. recipientKey]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    private static byte[] AssociatedData(string recipientKeyId) => Encoding.ASCII.GetBytes($"{SealedFindings.FormatName}\n{recipientKeyId}");
}
