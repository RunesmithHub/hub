using System.Security.Cryptography;
using RunesmithHub.Protocol.Signing;
using RunesmithHub.Tool.Findings;

namespace RunesmithHub.Tool.Tests.Analysis;

public sealed class FindingsCipherTests
{
    private static readonly byte[] Findings = """{ "heuristics": [] }"""u8.ToArray();

    private static (ECDiffieHellman PrivateKey, PublicKey PublicKey) Recipient()
    {
        var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return (key, new PublicKey { Scheme = PublicKey.EcdsaP256Sha256, SubjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo() });
    }

    [Fact]
    public void OpensWhatItSealedWithTheRecipientsKey()
    {
        var (privateKey, publicKey) = Recipient();
        using var _ = privateKey;

        var sealedFindings = FindingsCipher.Seal(Findings, publicKey);

        Assert.Equal(SealedFindings.FormatName, sealedFindings.Format);
        Assert.Equal(publicKey.KeyId, sealedFindings.Recipient);
        Assert.NotEqual(Findings, sealedFindings.Ciphertext);
        Assert.Equal(Findings, FindingsCipher.Open(sealedFindings, privateKey));
    }

    [Fact]
    public void RefusesAnotherKey()
    {
        var (privateKey, publicKey) = Recipient();
        var (otherKey, _) = Recipient();
        using var first = privateKey;
        using var second = otherKey;

        var exception = Assert.Throws<ToolException>(() => FindingsCipher.Open(FindingsCipher.Seal(Findings, publicKey), otherKey));

        Assert.Contains("sealed to the key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesChangedFindings()
    {
        var (privateKey, publicKey) = Recipient();
        using var _ = privateKey;
        var sealedFindings = FindingsCipher.Seal(Findings, publicKey);
        var changed = sealedFindings with { Ciphertext = [.. sealedFindings.Ciphertext.Select((value, index) => index == 0 ? (byte)(value ^ 1) : value)] };

        Assert.Throws<ToolException>(() => FindingsCipher.Open(changed, privateKey));
    }
}
