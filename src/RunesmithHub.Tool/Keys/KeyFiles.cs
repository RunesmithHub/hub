using System.Security.Cryptography;
using System.Text;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Signing;

namespace RunesmithHub.Tool.Keys;

/// <summary>Reads and writes private key files (PKCS#8 PEM) and public key files (the root's key JSON).</summary>
internal static class KeyFiles
{
    private const string EncryptedLabel = "-----BEGIN ENCRYPTED PRIVATE KEY-----";
    private const string P256Oid = "1.2.840.10045.3.1.7";

    /// <summary>Gets the public key file that belongs beside a private key file: <c>publish.pem</c> gives <c>publish.pub.json</c>.</summary>
    public static string PublicPathFor(string privateKeyPath) =>
        Path.ChangeExtension(privateKeyPath, null) + ".pub.json";

    /// <summary>Writes a private key file that only its owner can read; never replaces an existing file.</summary>
    public static void WritePrivateKey(string path, string pem)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        stream.Write(Encoding.ASCII.GetBytes(pem.EndsWith('\n') ? pem : pem + "\n"));
    }

    public static void WritePublicKey(string path, PublicKey key)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        stream.Write(IndexJson.Serialize(key));
        stream.WriteByte((byte)'\n');
    }

    /// <exception cref="InvalidDataException">The file is not a P-256 public key.</exception>
    public static PublicKey ReadPublicKey(string path) => ParsePublicKey(File.ReadAllBytes(path), path);

    /// <summary>Reads a public key in the format of public key files.</summary>
    /// <param name="source">Where the key came from, for the error message.</param>
    /// <exception cref="InvalidDataException">The JSON is not a P-256 public key.</exception>
    public static PublicKey ParsePublicKey(ReadOnlySpan<byte> json, string source)
    {
        var key = IndexJson.Deserialize<PublicKey>(json);
        if (key.Scheme != PublicKey.EcdsaP256Sha256 || !IsP256(key))
            throw new InvalidDataException($"{source} is not an {PublicKey.EcdsaP256Sha256} public key.");
        return key;
    }

    /// <summary>Reads a private key, asking for the passphrase only when the file is encrypted.</summary>
    public static EcdsaSigner ReadSigner(string path, Func<string> passphrase)
    {
        var pem = File.ReadAllText(path);
        return pem.Contains(EncryptedLabel, StringComparison.Ordinal) ? EcdsaSigner.FromEncryptedPem(pem, passphrase()) : EcdsaSigner.FromPem(pem);
    }

    /// <summary>Reads a private key for key agreement, asking for the passphrase only when the file is encrypted.</summary>
    /// <exception cref="CryptographicException">The passphrase is wrong or the file is not a P-256 private key.</exception>
    public static ECDiffieHellman ReadKeyAgreementKey(string path, Func<string> passphrase)
    {
        var pem = File.ReadAllText(path);
        // Windows will not load a signing key as a key-agreement key directly; going through the key's parameters works everywhere.
        using var signingKey = ECDsa.Create();
        if (pem.Contains(EncryptedLabel, StringComparison.Ordinal))
            signingKey.ImportFromEncryptedPem(pem, passphrase());
        else
            signingKey.ImportFromPem(pem);
        var parameters = signingKey.ExportParameters(includePrivateParameters: true);
        return parameters.Curve.Oid.Value == P256Oid ? ECDiffieHellman.Create(parameters) : throw new CryptographicException($"{path} is not a P-256 private key.");
    }

    private static bool IsP256(PublicKey key)
    {
        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportSubjectPublicKeyInfo(key.SubjectPublicKeyInfo.Span, out _);
            return PublicKey.FromEcdsa(ecdsa).KeyId == key.KeyId;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
