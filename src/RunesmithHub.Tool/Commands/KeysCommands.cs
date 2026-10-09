using System.CommandLine;
using System.Globalization;
using RunesmithHub.Protocol.Signing;
using RunesmithHub.Tool.Console;
using RunesmithHub.Tool.Keys;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub keys</c>: offline key and root management for key ceremonies.</summary>
internal static class KeysCommands
{
    public static Command Create(ToolContext context)
    {
        var keys = new Command("keys", "Manage signing keys and the root of trust.");
        keys.Add(Generate(context));

        var root = new Command("root", "Create, sign and verify root metadata.");
        root.Add(RootInit(context));
        root.Add(RootSign(context));
        root.Add(RootVerify(context));
        keys.Add(root);
        return keys;
    }

    private static Option<string> PassphraseEnvironment() =>
        new("--passphrase-env") { Description = "The environment variable that holds the passphrase, instead of asking for it." };

    private static Command Generate(ToolContext context)
    {
        var output = new Option<string>("--out") { Description = "The private key file to write.", Required = true };
        var publicOutput = new Option<string>("--public-out") { Description = "The public key file to write; next to the private key by default." };
        var passphraseEnvironment = PassphraseEnvironment();
        var noPassphrase = new Option<bool>("--no-passphrase") { Description = "Write the private key unencrypted, for an online key that goes straight into a secret store." };
        var command = new Command("generate", "Generate a P-256 key, encrypted with a passphrase.") { output, publicOutput, passphraseEnvironment, noPassphrase };
        command.SetAction(result => CommandRunner.Run(result, writer =>
        {
            var privatePath = result.GetRequiredValue(output);
            var publicPath = result.GetValue(publicOutput) ?? KeyFiles.PublicPathFor(privatePath);
            if (File.Exists(privatePath) || File.Exists(publicPath))
                throw new ToolException($"{(File.Exists(privatePath) ? privatePath : publicPath)} already exists.");

            if (result.GetValue(noPassphrase) && result.GetValue(passphraseEnvironment) is not null)
                throw new ToolException("Use either --no-passphrase or --passphrase-env, not both.");

            using var signer = EcdsaSigner.Generate();
            var pem = result.GetValue(noPassphrase)
                ? signer.ExportPem()
                : signer.ExportEncryptedPem(new PassphraseSource(context).ForNewKey(result.GetValue(passphraseEnvironment)));
            KeyFiles.WritePrivateKey(privatePath, pem);
            KeyFiles.WritePublicKey(publicPath, signer.PublicKey);
            writer.WriteLine($"Key ID: {signer.KeyId}");
            writer.WriteLine($"Private key: {privatePath}");
            writer.WriteLine($"Public key: {publicPath}");
            return 0;
        }));
        return command;
    }

    private static Command RootInit(ToolContext context)
    {
        Option<string[]> Keys(string role) => new($"--{role}-key") { Description = $"A public key file for the {role} role; repeat for more.", Required = true, AllowMultipleArgumentsPerToken = false };
        Option<int> Threshold(string role, int fallback) => new($"--{role}-threshold") { Description = $"How many {role} keys must sign.", DefaultValueFactory = _ => fallback };

        var rootKeys = Keys("root");
        var publishKeys = Keys("publish");
        var moderationKeys = Keys("moderation");
        var timestampKeys = Keys("timestamp");
        var rootThreshold = Threshold("root", 2);
        var publishThreshold = Threshold("publish", 1);
        var moderationThreshold = Threshold("moderation", 1);
        var timestampThreshold = Threshold("timestamp", 1);
        var version = new Option<int>("--version") { Description = "The root version.", DefaultValueFactory = _ => 1 };
        var expiresDays = new Option<int>("--expires-days") { Description = "How many days from now the root expires.", DefaultValueFactory = _ => 365 };
        var output = new Option<string>("--out") { Description = "The unsigned root file to write.", Required = true };

        var command = new Command("init", "Create an unsigned root from public key files.")
        {
            rootKeys, publishKeys, moderationKeys, timestampKeys, rootThreshold, publishThreshold, moderationThreshold, timestampThreshold, version, expiresDays, output,
        };
        command.SetAction(result => CommandRunner.Run(result, writer =>
        {
            RoleTemplate Role(Option<string[]> files, Option<int> threshold) =>
                new([.. result.GetRequiredValue(files).Select(KeyFiles.ReadPublicKey)], result.GetValue(threshold));

            var template = new RootTemplate(
                result.GetValue(version),
                context.Time.GetUtcNow().AddDays(result.GetValue(expiresDays)),
                Role(rootKeys, rootThreshold),
                Role(publishKeys, publishThreshold),
                Role(moderationKeys, moderationThreshold),
                Role(timestampKeys, timestampThreshold));
            var path = result.GetRequiredValue(output);
            if (File.Exists(path))
                throw new ToolException($"{path} already exists.");

            File.WriteAllBytes(path, RootCeremony.Create(template));
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Wrote unsigned root version {template.Version} to {path}, expiring {template.Expires:yyyy-MM-dd}."));
            return 0;
        }));
        return command;
    }

    private static Command RootSign(ToolContext context)
    {
        var file = new Argument<string>("root") { Description = "The root file to sign; the signature is added in place." };
        var key = new Option<string>("--key") { Description = "The private key file to sign with.", Required = true };
        var passphraseEnvironment = PassphraseEnvironment();
        var command = new Command("sign", "Add a signature to a root.") { file, key, passphraseEnvironment };
        command.SetAction(result => CommandRunner.Run(result, writer =>
        {
            var keyPath = result.GetRequiredValue(key);
            using var signer = KeyFiles.ReadSigner(keyPath, () => new PassphraseSource(context).ForExistingKey(result.GetValue(passphraseEnvironment), keyPath));
            var path = result.GetRequiredValue(file);
            var (envelope, root) = RootCeremony.Sign(File.ReadAllBytes(path), signer);
            File.WriteAllBytes(path, envelope);

            var listed = root.Roles.Root.KeyIds.Contains(signer.KeyId) ? "a root key of this root" : "not a root key of this root, so it counts for the previous root";
            writer.WriteLine($"Signed root version {root.Version} with {signer.KeyId} ({listed}).");
            return 0;
        }));
        return command;
    }

    private static Command RootVerify(ToolContext context)
    {
        var directory = new Argument<string>("directory") { Description = "The folder with the roots, named 1.json, 2.json and so on." };
        var command = new Command("verify", "Verify a chain of roots as Runesmith walks it.") { directory };
        command.SetAction(result => CommandRunner.Run(result, writer =>
        {
            var report = RootCeremony.Verify(result.GetRequiredValue(directory), context.Time.GetUtcNow());
            foreach (var line in report.Lines)
                writer.WriteLine(line);
            return report.Valid ? 0 : 1;
        }));
        return command;
    }
}
