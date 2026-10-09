using System.Security.Cryptography;

namespace RunesmithHub.Protocol.Signing;

/// <summary>Signs with a P-256 private key held in memory.</summary>
public sealed class EcdsaSigner : ISigner, IDisposable
{
    private const int Pbkdf2Iterations = 600_000;

    private readonly ECDsa _key;

    private EcdsaSigner(ECDsa key)
    {
        PublicKey.EnsureP256(key);
        _key = key;
        PublicKey = PublicKey.FromEcdsa(key);
        KeyId = PublicKey.KeyId;
    }

    public string KeyId { get; }

    public PublicKey PublicKey { get; }

    /// <summary>Generates a new P-256 key.</summary>
    public static EcdsaSigner Generate() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>Reads an unencrypted PKCS#8 PEM private key.</summary>
    /// <exception cref="CryptographicException">The text is not a P-256 private key.</exception>
    public static EcdsaSigner FromPem(string pem) => Import(key => key.ImportFromPem(pem));

    /// <summary>Reads a passphrase-encrypted PKCS#8 PEM private key.</summary>
    /// <exception cref="CryptographicException">The passphrase is wrong or the text is not a P-256 private key.</exception>
    public static EcdsaSigner FromEncryptedPem(string pem, ReadOnlySpan<char> passphrase)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportFromEncryptedPem(pem, passphrase);
            return new EcdsaSigner(key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    private static EcdsaSigner Import(Action<ECDsa> import)
    {
        var key = ECDsa.Create();
        try
        {
            import(key);
            return new EcdsaSigner(key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>Exports the private key as unencrypted PKCS#8 PEM.</summary>
    public string ExportPem() => _key.ExportPkcs8PrivateKeyPem();

    /// <summary>Exports the private key as PKCS#8 PEM encrypted with AES-256 and a PBKDF2-SHA256 key from the passphrase.</summary>
    public string ExportEncryptedPem(ReadOnlySpan<char> passphrase) =>
        _key.ExportEncryptedPkcs8PrivateKeyPem(passphrase, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, Pbkdf2Iterations));

    public byte[] Sign(ReadOnlySpan<byte> data) =>
        _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public void Dispose() => _key.Dispose();
}
