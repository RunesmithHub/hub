using System.Text;

namespace RunesmithHub.Tool.Registrations;

/// <summary>Finds names that look like another: the same after replacing confusable characters, a small edit away, or the same with a word added.</summary>
internal static class NameSimilarity
{
    private static readonly string[] AddedWords = ["official", "pro", "plus", "hub", "app", "dev", "team", "tools", "real", "the", "new", "free", "premium"];

    private static readonly (string From, string To)[] Confusables =
    [
        ("rn", "m"), ("vv", "w"), ("cl", "d"), ("0", "o"), ("1", "l"), ("i", "l"), ("|", "l"), ("3", "e"), ("4", "a"), ("5", "s"), ("7", "t"), ("8", "b"),
        ("9", "g"), ("$", "s"), ("@", "a"),
    ];

    /// <summary>Explains how a candidate resembles an existing name, or returns null when it does not.</summary>
    public static string? Compare(string candidate, string existing)
    {
        var a = Normalize(candidate);
        var b = Normalize(existing);
        if (a.Length == 0 || b.Length == 0)
            return null;
        if (a == b)
            return $"is the same as \"{existing}\"";

        var sa = Skeleton(a);
        var sb = Skeleton(b);
        if (sa == sb)
            return $"looks the same as \"{existing}\"";
        var allowed = Math.Min(sa.Length, sb.Length) >= 8 ? 2 : 1;
        if (Distance(sa, sb) <= allowed)
            return $"is within {allowed} character{(allowed == 1 ? "" : "s")} of \"{existing}\"";
        foreach (var word in AddedWords)
        {
            if (sa == Skeleton(sb + word) || sa == Skeleton(word + sb))
                return $"adds \"{word}\" to \"{existing}\"";
        }

        return null;
    }

    /// <summary>Lowercases and drops separators, so <c>Lumen Labs</c> and <c>lumen-labs</c> compare equal.</summary>
    public static string Normalize(string name) =>
        new([.. name.ToLowerInvariant().Where(c => c is not ('-' or '_' or '.' or ' '))]);

    /// <summary>Replaces characters that look alike with one representative.</summary>
    public static string Skeleton(string normalized)
    {
        var text = new StringBuilder(normalized);
        foreach (var (from, to) in Confusables)
            text.Replace(from, to);
        return text.ToString();
    }

    /// <summary>Gets the Levenshtein distance between two strings.</summary>
    public static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
