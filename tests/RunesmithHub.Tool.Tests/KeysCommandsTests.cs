using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Signing;
using RunesmithHub.Tool.Keys;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests;

public sealed class KeysCommandsTests : IDisposable
{
    private const string Passphrase = "a long enough passphrase";

    private readonly TemporaryFolder _folder = new();
    private readonly ToolRunner _tool = new();

    public KeysCommandsTests() => _tool.Environment["KEY_PASSPHRASE"] = Passphrase;

    public void Dispose() => _folder.Dispose();

    private async Task<string> GenerateAsync(string name)
    {
        var (exitCode, _, error) = await _tool.RunAsync("keys", "generate", "--out", _folder.Combine(name + ".pem"), "--passphrase-env", "KEY_PASSPHRASE");
        Assert.True(exitCode == 0, error);
        return _folder.Combine(name + ".pub.json");
    }

    private async Task<string> InitRootAsync(string output, int version = 1, int days = 365, string rootPrefix = "root")
    {
        string[] args =
        [
            "keys", "root", "init",
            "--root-key", _folder.Combine($"{rootPrefix}-1.pub.json"), "--root-key", _folder.Combine($"{rootPrefix}-2.pub.json"), "--root-key", _folder.Combine($"{rootPrefix}-3.pub.json"),
            "--publish-key", _folder.Combine("publish.pub.json"), "--moderation-key", _folder.Combine("moderation.pub.json"), "--timestamp-key", _folder.Combine("timestamp.pub.json"),
            "--version", version.ToString(System.Globalization.CultureInfo.InvariantCulture), "--expires-days", days.ToString(System.Globalization.CultureInfo.InvariantCulture), "--out", output,
        ];
        var (exitCode, _, error) = await _tool.RunAsync(args);
        Assert.True(exitCode == 0, error);
        return output;
    }

    private async Task SignAsync(string root, string key)
    {
        var (exitCode, _, error) = await _tool.RunAsync("keys", "root", "sign", root, "--key", _folder.Combine(key + ".pem"), "--passphrase-env", "KEY_PASSPHRASE");
        Assert.True(exitCode == 0, error);
    }

    private async Task GenerateCeremonyKeysAsync(string rootPrefix = "root")
    {
        foreach (var name in new[] { $"{rootPrefix}-1", $"{rootPrefix}-2", $"{rootPrefix}-3" })
            await GenerateAsync(name);
        if (!File.Exists(_folder.Combine("publish.pem")))
        {
            foreach (var name in new[] { "publish", "moderation", "timestamp" })
                await GenerateAsync(name);
        }
    }

    [Fact]
    public async Task GeneratesAnEncryptedKeyAndItsPublicKey()
    {
        var (exitCode, output, _) = await _tool.RunAsync("keys", "generate", "--out", _folder.Combine("publish.pem"), "--passphrase-env", "KEY_PASSPHRASE");

        var pem = await File.ReadAllTextAsync(_folder.Combine("publish.pem"), TestContext.Current.CancellationToken);
        using var signer = EcdsaSigner.FromEncryptedPem(pem, Passphrase);
        var publicKey = KeyFiles.ReadPublicKey(_folder.Combine("publish.pub.json"));
        Assert.Equal(0, exitCode);
        Assert.StartsWith("-----BEGIN ENCRYPTED PRIVATE KEY-----", pem, StringComparison.Ordinal);
        Assert.Contains($"Key ID: {signer.KeyId}", output, StringComparison.Ordinal);
        Assert.Equal(signer.KeyId, publicKey.KeyId);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_folder.Combine("publish.pem")));
    }

    [Fact]
    public async Task GeneratesAnUnencryptedKeyForSecretStores()
    {
        var (exitCode, _, error) = await _tool.RunAsync("keys", "generate", "--out", _folder.Combine("timestamp.pem"), "--no-passphrase");

        var pem = await File.ReadAllTextAsync(_folder.Combine("timestamp.pem"), TestContext.Current.CancellationToken);
        using var signer = EcdsaSigner.FromPem(pem);
        Assert.True(exitCode == 0, error);
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", pem, StringComparison.Ordinal);
        Assert.Equal(signer.KeyId, KeyFiles.ReadPublicKey(_folder.Combine("timestamp.pub.json")).KeyId);
    }

    [Fact]
    public async Task RefusesNoPassphraseTogetherWithAPassphraseVariable()
    {
        var (exitCode, _, error) = await _tool.RunAsync("keys", "generate", "--out", _folder.Combine("both.pem"), "--no-passphrase", "--passphrase-env", "KEY_PASSPHRASE");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("not both", error, StringComparison.Ordinal);
        Assert.False(File.Exists(_folder.Combine("both.pem")));
    }

    [Fact]
    public async Task AsksForTheNewPassphraseTwiceAtTheTerminal()
    {
        _tool.Answers.Enqueue(Passphrase);
        _tool.Answers.Enqueue("something else entirely");

        var (exitCode, _, error) = await _tool.RunAsync("keys", "generate", "--out", _folder.Combine("a.pem"));

        Assert.Equal(1, exitCode);
        Assert.Contains("passphrases differ", error, StringComparison.Ordinal);
        Assert.False(File.Exists(_folder.Combine("a.pem")));
    }

    [Fact]
    public async Task RefusesShortPassphrasesMissingVariablesAndExistingFiles()
    {
        _tool.Environment["SHORT"] = "short";
        await GenerateAsync("existing");

        Assert.Contains("at least 12", (await _tool.RunAsync("keys", "generate", "--out", _folder.Combine("a.pem"), "--passphrase-env", "SHORT")).Error, StringComparison.Ordinal);
        Assert.Contains("MISSING is not set", (await _tool.RunAsync("keys", "generate", "--out", _folder.Combine("b.pem"), "--passphrase-env", "MISSING")).Error, StringComparison.Ordinal);
        Assert.Contains("already exists", (await _tool.RunAsync("keys", "generate", "--out", _folder.Combine("existing.pem"), "--passphrase-env", "KEY_PASSPHRASE")).Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatesSignsAndVerifiesTheFirstRoot()
    {
        await GenerateCeremonyKeysAsync();
        var roots = Directory.CreateDirectory(_folder.Combine("roots")).FullName;
        var root = await InitRootAsync(Path.Combine(roots, "1.json"));

        var unsigned = await _tool.RunAsync("keys", "root", "verify", roots);
        await SignAsync(root, "root-1");
        var once = await _tool.RunAsync("keys", "root", "verify", roots);
        await SignAsync(root, "root-3");
        var twice = await _tool.RunAsync("keys", "root", "verify", roots);

        var payload = RootChain.Read(await File.ReadAllBytesAsync(root, TestContext.Current.CancellationToken)).Root;
        Assert.Equal(1, unsigned.ExitCode);
        Assert.Equal(1, once.ExitCode);
        Assert.Contains("not signed by enough of its own root keys", once.Output, StringComparison.Ordinal);
        Assert.Equal(0, twice.ExitCode);
        Assert.Contains("Root 1: valid, signed by 2 of its 3 root keys, expires 2027-10-09.", twice.Output, StringComparison.Ordinal);
        Assert.Equal(2, payload.Roles.Root.Threshold);
        Assert.Equal(6, payload.Keys.Count);
        Assert.Equal([KeyFiles.ReadPublicKey(_folder.Combine("publish.pub.json")).KeyId], payload.Roles.Publish.KeyIds);
    }

    [Fact]
    public async Task VerifiesARotationOnlyWhenOldAndNewRootKeysSigned()
    {
        await GenerateCeremonyKeysAsync();
        await GenerateCeremonyKeysAsync("new-root");
        var roots = Directory.CreateDirectory(_folder.Combine("roots")).FullName;
        var first = await InitRootAsync(Path.Combine(roots, "1.json"));
        await SignAsync(first, "root-1");
        await SignAsync(first, "root-2");
        var second = await InitRootAsync(Path.Combine(roots, "2.json"), version: 2, rootPrefix: "new-root");

        await SignAsync(second, "root-1");
        await SignAsync(second, "root-2");
        var oldOnly = await _tool.RunAsync("keys", "root", "verify", roots);
        await SignAsync(second, "new-root-2");
        await SignAsync(second, "new-root-3");
        var both = await _tool.RunAsync("keys", "root", "verify", roots);

        Assert.Equal(1, oldOnly.ExitCode);
        Assert.Contains("2.json: Root version 2 is not signed by enough of its own root keys.", oldOnly.Output, StringComparison.Ordinal);
        Assert.Equal(0, both.ExitCode);
        Assert.Contains("Root 2: valid", both.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenASignatureCountsForThePreviousRoot()
    {
        await GenerateCeremonyKeysAsync();
        await GenerateAsync("outsider");
        var root = await InitRootAsync(_folder.Combine("2.json"), version: 2);

        var (_, output, _) = await _tool.RunAsync("keys", "root", "sign", root, "--key", _folder.Combine("outsider.pem"), "--passphrase-env", "KEY_PASSPHRASE");

        Assert.Contains("counts for the previous root", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignsWithAnUnencryptedKeyWithoutAPassphrase()
    {
        await GenerateCeremonyKeysAsync();
        var root = await InitRootAsync(_folder.Combine("1.json"));
        using var plain = EcdsaSigner.Generate();
        KeyFiles.WritePrivateKey(_folder.Combine("plain.pem"), plain.ExportPem());

        var (exitCode, _, error) = await _tool.RunAsync("keys", "root", "sign", root, "--key", _folder.Combine("plain.pem"));

        Assert.True(exitCode == 0, error);
        var envelope = IndexJson.Deserialize<DsseEnvelope>(await File.ReadAllBytesAsync(root, TestContext.Current.CancellationToken));
        Assert.Equal(plain.KeyId, Assert.Single(envelope.Signatures).KeyId);
    }

    [Fact]
    public async Task ReportsAnExpiredFinalRoot()
    {
        await GenerateCeremonyKeysAsync();
        var roots = Directory.CreateDirectory(_folder.Combine("roots")).FullName;
        var root = await InitRootAsync(Path.Combine(roots, "1.json"), days: 10);
        await SignAsync(root, "root-1");
        await SignAsync(root, "root-2");
        _tool.Clock.Advance(TimeSpan.FromDays(11));

        var (exitCode, output, _) = await _tool.RunAsync("keys", "root", "verify", roots);

        Assert.Equal(1, exitCode);
        Assert.Contains("Root 1 expired at 2026-10-19", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsThresholdsAboveTheNumberOfKeys()
    {
        await GenerateCeremonyKeysAsync();

        var (exitCode, _, error) = await _tool.RunAsync(
            "keys", "root", "init", "--root-key", _folder.Combine("root-1.pub.json"), "--publish-key", _folder.Combine("publish.pub.json"),
            "--moderation-key", _folder.Combine("moderation.pub.json"), "--timestamp-key", _folder.Combine("timestamp.pub.json"), "--out", _folder.Combine("1.json"));

        Assert.Equal(1, exitCode);
        Assert.Contains("root threshold must be between 1 and 1", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAFolderWithoutRoots()
    {
        var (exitCode, output, _) = await _tool.RunAsync("keys", "root", "verify", _folder.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("has no 1.json", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePublicKeyFileSitsNextToThePrivateKey()
    {
        Assert.Equal(Path.Combine("keys", "publish.pub.json"), KeyFiles.PublicPathFor(Path.Combine("keys", "publish.pem")));
    }
}
