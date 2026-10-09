using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Signing;

/// <summary>A role key as the root lists it: the scheme and the SubjectPublicKeyInfo DER bytes.</summary>
public sealed record PublicKey
{
    /// <summary>The only scheme the hub uses: ECDSA over P-256 with SHA-256, signatures in IEEE P1363 form.</summary>
    public const string EcdsaP256Sha256 = "ecdsa-p256-sha256";

    private const string P256Oid = "1.2.840.10045.3.1.7";

    public required string Scheme { get; init; }

    /// <summary>Gets the SubjectPublicKeyInfo DER bytes; base64 in JSON.</summary>
    [JsonPropertyName("public")]
    public required ReadOnlyMemory<byte> SubjectPublicKeyInfo { get; init; }

    /// <summary>Gets the key ID: the lowercase hex SHA-256 of the SubjectPublicKeyInfo DER bytes.</summary>
    [JsonIgnore]
    public string KeyId => Hashes.Sha256(SubjectPublicKeyInfo.Span);

    /// <summary>Describes the public half of a P-256 key.</summary>
    /// <exception cref="CryptographicException">The key is not on P-256.</exception>
    public static PublicKey FromEcdsa(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        EnsureP256(key);
        return new PublicKey { Scheme = EcdsaP256Sha256, SubjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo() };
    }

    /// <summary>Checks a P1363 signature over data. Returns false for any other scheme, curve or malformed key.</summary>
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (!string.Equals(Scheme, EcdsaP256Sha256, StringComparison.Ordinal) || signature.Length != 64)
            return false;

        using var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(SubjectPublicKeyInfo.Span, out var read);
            if (read != SubjectPublicKeyInfo.Length || !IsP256(key))
                return false;
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    internal static void EnsureP256(ECDsa key)
    {
        if (!IsP256(key))
            throw new CryptographicException("The key is not an ECDSA P-256 key.");
    }

    private static bool IsP256(ECDsa key) =>
        key.KeySize == 256 && string.Equals(key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value, P256Oid, StringComparison.Ordinal);
}
