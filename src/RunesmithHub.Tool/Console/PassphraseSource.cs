namespace RunesmithHub.Tool.Console;

/// <summary>Gets a key's passphrase from a named environment variable, or else by asking at the terminal.</summary>
internal sealed class PassphraseSource(ToolContext context)
{
    private const int MinimumLength = 12;

    /// <summary>Gets the passphrase for an existing key.</summary>
    /// <exception cref="ToolException">No passphrase could be read.</exception>
    public string ForExistingKey(string? environmentVariable, string keyFile) =>
        FromEnvironment(environmentVariable) ?? context.Prompt.Ask($"Passphrase for {keyFile}: ")
        ?? throw new ToolException("No passphrase: set --passphrase-env or run in a terminal.");

    /// <summary>Gets the passphrase for a new key, asking twice at the terminal.</summary>
    /// <exception cref="ToolException">No passphrase could be read, the two entries differ, or it is too short.</exception>
    public string ForNewKey(string? environmentVariable)
    {
        var passphrase = FromEnvironment(environmentVariable);
        if (passphrase is null)
        {
            passphrase = context.Prompt.Ask("New passphrase: ") ?? throw new ToolException("No passphrase: set --passphrase-env or run in a terminal.");
            if (context.Prompt.Ask("Repeat the passphrase: ") != passphrase)
                throw new ToolException("The passphrases differ.");
        }

        return passphrase.Length >= MinimumLength ? passphrase : throw new ToolException($"The passphrase must have at least {MinimumLength} characters.");
    }

    private string? FromEnvironment(string? variable)
    {
        if (variable is null)
            return null;
        var value = context.GetEnvironmentVariable(variable);
        return string.IsNullOrEmpty(value) ? throw new ToolException($"The environment variable {variable} is not set.") : value;
    }
}
