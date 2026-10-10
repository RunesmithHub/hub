using System.Text;

namespace RunesmithHub.Tool;

/// <summary>
/// Writes text to a GitHub Actions log so that the runner reads no line of it as a workflow command: a line that would start with
/// <c>::</c> or <c>##[</c> after leading white space is written with a space after its first character.
/// </summary>
internal sealed class ActionsLogWriter(TextWriter inner) : TextWriter
{
    private static readonly string[] CommandPrefixes = ["::", "##["];

    private readonly StringBuilder _pending = new();
    private bool _decided;

    public override Encoding Encoding => inner.Encoding;

    /// <summary>Replaces the console's output and error with writers like this one when running in GitHub Actions.</summary>
    /// <returns>The writers to complete before the process ends, or none outside GitHub Actions.</returns>
    public static IReadOnlyList<ActionsLogWriter> InstallForConsole(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!string.Equals(environment("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
            return [];
        var output = new ActionsLogWriter(System.Console.Out);
        var error = new ActionsLogWriter(System.Console.Error);
        System.Console.SetOut(output);
        System.Console.SetError(error);
        return [output, error];
    }

    /// <summary>Makes a line safe to write; <paramref name="line"/> has no line breaks.</summary>
    public static string Neutralize(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var start = FirstVisible(line);
        return start >= 0 && CommandPrefixes.Any(prefix => line.AsSpan(start).StartsWith(prefix, StringComparison.Ordinal))
            ? line.Insert(start + 1, " ")
            : line;
    }

    public override void Write(char value)
    {
        if (value is '\n' or '\r')
        {
            if (!_decided)
                inner.Write(Neutralize(_pending.ToString()));
            _pending.Clear();
            _decided = false;
            inner.Write(value);
            return;
        }

        if (_decided)
        {
            inner.Write(value);
            return;
        }

        _pending.Append(value);
        var text = _pending.ToString();
        var start = FirstVisible(text);
        if (start < 0 || CommandPrefixes.Any(prefix => prefix.Length > text.Length - start && prefix.StartsWith(text[start..], StringComparison.Ordinal)))
            return;
        inner.Write(Neutralize(text));
        _pending.Clear();
        _decided = true;
    }

    public override void Write(string? value)
    {
        foreach (var c in value ?? "")
            Write(c);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        for (var i = index; i < index + count; i++)
            Write(buffer[i]);
    }

    public override void Flush() => inner.Flush();

    /// <summary>Writes what is left of an unfinished line and flushes.</summary>
    public void Complete()
    {
        if (!_decided && _pending.Length > 0)
            inner.Write(Neutralize(_pending.ToString()) + "\n");
        _pending.Clear();
        _decided = false;
        inner.Flush();
    }

    private static int FirstVisible(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                return i;
        }

        return -1;
    }
}
