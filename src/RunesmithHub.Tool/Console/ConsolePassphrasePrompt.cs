using System.Text;

namespace RunesmithHub.Tool.Console;

/// <summary>Reads a passphrase from the terminal without showing it.</summary>
internal sealed class ConsolePassphrasePrompt : IPassphrasePrompt
{
    public string? Ask(string prompt)
    {
        if (System.Console.IsInputRedirected)
            return null;

        System.Console.Error.Write(prompt);
        var passphrase = new StringBuilder();
        while (true)
        {
            var key = System.Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (passphrase.Length > 0)
                    passphrase.Length--;
                continue;
            }

            if (!char.IsControl(key.KeyChar))
                passphrase.Append(key.KeyChar);
        }

        System.Console.Error.WriteLine();
        return passphrase.ToString();
    }
}
