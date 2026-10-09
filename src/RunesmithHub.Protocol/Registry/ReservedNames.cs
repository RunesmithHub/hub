namespace RunesmithHub.Protocol.Registry;

/// <summary>Reads <c>policy/reserved-names.txt</c>: one name per line, with <c>#</c> starting a comment.</summary>
public static class ReservedNames
{
    public static IReadOnlySet<string> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            var name = (hash < 0 ? line : line[..hash]).Trim();
            if (name.Length > 0)
                names.Add(name);
        }

        return names;
    }
}
