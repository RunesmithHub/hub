namespace RunesmithHub.Tool.Console;

/// <summary>Asks the person at the terminal for a passphrase.</summary>
internal interface IPassphrasePrompt
{
    /// <summary>Asks for a passphrase without echoing it, or returns null when there is no terminal to ask.</summary>
    string? Ask(string prompt);
}
