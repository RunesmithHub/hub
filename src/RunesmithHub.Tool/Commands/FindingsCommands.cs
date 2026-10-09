using System.CommandLine;
using System.Text;
using RunesmithHub.Tool.Console;
using RunesmithHub.Tool.Findings;
using RunesmithHub.Tool.Keys;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub findings</c>: review findings for administrators.</summary>
internal static class FindingsCommands
{
    public static Command Create(ToolContext context)
    {
        var findings = new Command("findings", "Review findings for administrators.");
        findings.Add(Decrypt(context));
        return findings;
    }

    private static Command Decrypt(ToolContext context)
    {
        var file = new Argument<string>("file") { Description = "The findings file from the analysis." };
        var key = new Option<string>("--key") { Description = "Your private key file, as rshub keys generate writes it.", Required = true };
        var passphraseEnvironment = new Option<string>("--passphrase-env") { Description = "The environment variable that holds the passphrase, instead of asking for it." };
        var command = new Command("decrypt", "Decrypt review findings for administrators and print them.") { file, key, passphraseEnvironment };
        command.SetAction(result => CommandRunner.Run(result, writer =>
        {
            var keyPath = result.GetRequiredValue(key);
            var sealedFindings = SealedFindings.Read(File.ReadAllBytes(result.GetRequiredValue(file)));
            using var privateKey = KeyFiles.ReadKeyAgreementKey(keyPath, () => new PassphraseSource(context).ForExistingKey(result.GetValue(passphraseEnvironment), keyPath));
            writer.Write(Encoding.UTF8.GetString(FindingsCipher.Open(sealedFindings, privateKey)));
            return 0;
        }));
        return command;
    }
}
