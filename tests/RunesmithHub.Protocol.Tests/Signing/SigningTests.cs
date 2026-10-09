using System.Security.Cryptography;
using System.Text;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Signing;
using RunesmithHub.Protocol.Tests.Support;

namespace RunesmithHub.Protocol.Tests.Signing;

public sealed class SigningTests : IDisposable
{
    private readonly TestHub _hub = new(Records.Time);

    public void Dispose() => _hub.Dispose();

    [Fact]
    public void EncodesThePayloadAsDssePae()
    {
        var pae = Dsse.PreAuthenticationEncoding("application/vnd.runesmith.hub+json", "{\"a\":1}"u8);

        Assert.Equal("DSSEv1 34 application/vnd.runesmith.hub+json 7 {\"a\":1}", Encoding.UTF8.GetString(pae));
    }

    [Fact]
    public void CountsMultiByteCharactersAsBytes()
    {
        var pae = Dsse.PreAuthenticationEncoding("t", Encoding.UTF8.GetBytes("é"));

        Assert.Equal("DSSEv1 1 t 2 é", Encoding.UTF8.GetString(pae));
    }

    [Fact]
    public void SignsWithP1363SignaturesThatVerifyAgainstThePublicKey()
    {
        using var signer = EcdsaSigner.Generate();
        var data = "hello"u8.ToArray();

        var signature = signer.Sign(data);

        Assert.Equal(64, signature.Length);
        Assert.True(signer.PublicKey.Verify(data, signature));
        Assert.False(signer.PublicKey.Verify("hellO"u8, signature));
    }

    [Fact]
    public void TheKeyIdIsTheSha256OfTheSubjectPublicKeyInfo()
    {
        using var signer = EcdsaSigner.Generate();

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(signer.PublicKey.SubjectPublicKeyInfo.Span)), signer.KeyId);
        Assert.Equal(PublicKey.EcdsaP256Sha256, signer.PublicKey.Scheme);
    }

    [Fact]
    public void RejectsKeysOnOtherCurves()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var key = new PublicKey { Scheme = PublicKey.EcdsaP256Sha256, SubjectPublicKeyInfo = p384.ExportSubjectPublicKeyInfo() };

        Assert.Throws<CryptographicException>(() => PublicKey.FromEcdsa(p384));
        Assert.Throws<CryptographicException>(() => EcdsaSigner.FromPem(p384.ExportPkcs8PrivateKeyPem()));
        Assert.False(key.Verify("x"u8, new byte[64]));
    }

    [Fact]
    public void RejectsOtherSchemesAndMalformedKeys()
    {
        using var signer = EcdsaSigner.Generate();
        var signature = signer.Sign("x"u8);

        Assert.False((signer.PublicKey with { Scheme = "ed25519" }).Verify("x"u8, signature));
        Assert.False((signer.PublicKey with { SubjectPublicKeyInfo = new byte[] { 1, 2, 3 } }).Verify("x"u8, signature));
    }

    [Fact]
    public void RoundTripsPrivateKeysAsPem()
    {
        using var signer = EcdsaSigner.Generate();

        using var plain = EcdsaSigner.FromPem(signer.ExportPem());
        var encryptedPem = signer.ExportEncryptedPem("correct horse");
        using var encrypted = EcdsaSigner.FromEncryptedPem(encryptedPem, "correct horse");

        Assert.Equal(signer.KeyId, plain.KeyId);
        Assert.Equal(signer.KeyId, encrypted.KeyId);
        Assert.Contains("BEGIN ENCRYPTED PRIVATE KEY", encryptedPem, StringComparison.Ordinal);
        Assert.ThrowsAny<CryptographicException>(() => EcdsaSigner.FromEncryptedPem(encryptedPem, "wrong"));
    }

    [Fact]
    public void AddingASignatureByTheSameKeyReplacesIt()
    {
        using var signer = EcdsaSigner.Generate();

        var envelope = Dsse.AddSignature(Dsse.Sign("{}"u8.ToArray(), signer), signer);

        Assert.Single(envelope.Signatures);
    }

    [Fact]
    public void ARoleNeedsItsThresholdOfDistinctListedKeys()
    {
        var root = _hub.Root;
        var payload = "{}"u8.ToArray();

        Assert.False(RoleVerifier.MeetsThreshold(Dsse.Sign(payload, _hub.RootKeys[0]), root, root.Roles.Root));
        Assert.True(RoleVerifier.MeetsThreshold(Dsse.Sign(payload, _hub.RootKeys[0], _hub.RootKeys[2]), root, root.Roles.Root));
        Assert.False(RoleVerifier.MeetsThreshold(Dsse.Sign(payload, _hub.RootKeys[0], _hub.PublishKey), root, root.Roles.Root));
    }

    [Fact]
    public void ARepeatedSignatureByOneKeyCountsOnce()
    {
        var root = _hub.Root;
        var signed = Dsse.Sign("{}"u8.ToArray(), _hub.RootKeys[0]);
        var doubled = signed with { Signatures = [signed.Signatures[0], signed.Signatures[0]] };

        Assert.Equal(1, RoleVerifier.CountValidSignatures(doubled, root, root.Roles.Root));
        Assert.False(RoleVerifier.MeetsThreshold(doubled, root, root.Roles.Root));
    }

    [Fact]
    public void ASignatureOverOtherBytesDoesNotCount()
    {
        var root = _hub.Root;
        var signed = Dsse.Sign("{}"u8.ToArray(), _hub.RootKeys[0], _hub.RootKeys[1]);
        var tampered = signed with { Payload = "{ }"u8.ToArray() };
        var retyped = signed with { PayloadType = "application/json" };

        Assert.Equal(0, RoleVerifier.CountValidSignatures(tampered, root, root.Roles.Root));
        Assert.Equal(0, RoleVerifier.CountValidSignatures(retyped, root, root.Roles.Root));
    }

    [Fact]
    public void AKeyListedUnderAnotherKeysIdDoesNotCount()
    {
        var root = _hub.Root;
        var impostor = _hub.NewKey();
        var keys = root.Keys.ToDictionary(pair => pair.Key, pair => pair.Value);
        keys[_hub.RootKeys[0].KeyId] = impostor.PublicKey;
        var forged = root with { Keys = keys };
        var envelope = Dsse.Sign("{}"u8.ToArray(), impostor);
        var relabelled = envelope with { Signatures = [envelope.Signatures[0] with { KeyId = _hub.RootKeys[0].KeyId }] };

        Assert.Equal(0, RoleVerifier.CountValidSignatures(relabelled, forged, forged.Roles.Root));
    }

    [Fact]
    public void AThresholdBelowOneNeverPasses()
    {
        var root = _hub.Root;
        var role = root.Roles.Publish with { Threshold = 0 };

        Assert.False(RoleVerifier.MeetsThreshold(Dsse.Sign("{}"u8.ToArray()), root, role));
    }

    [Fact]
    public void HasSameKeysComparesKeyIdsAndThreshold()
    {
        var role = new RoleKeys { KeyIds = ["a", "b"], Threshold = 1 };

        Assert.True(role.HasSameKeys(new RoleKeys { KeyIds = ["b", "a"], Threshold = 1 }));
        Assert.False(role.HasSameKeys(new RoleKeys { KeyIds = ["a"], Threshold = 1 }));
        Assert.False(role.HasSameKeys(new RoleKeys { KeyIds = ["a", "b"], Threshold = 2 }));
    }
}
