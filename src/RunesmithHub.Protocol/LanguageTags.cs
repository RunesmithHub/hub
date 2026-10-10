namespace RunesmithHub.Protocol;

/// <summary>
/// BCP 47 language tags as plugins and language packs use them: a language with an optional script, region and variants, such as
/// <c>en</c>, <c>nl-BE</c>, <c>pt-BR</c>, <c>zh-Hant-TW</c> or <c>ca-valencia</c>. Tags are compared case-insensitively and written in
/// canonical case.
/// </summary>
public static class LanguageTags
{
    /// <summary>The tag of English, the language of every localized plugin's source.</summary>
    public const string English = "en";

    /// <summary>The longest tag, the length BCP 47 asks every implementation to support.</summary>
    public const int MaxLength = 35;

    /// <summary>Gets the comparer tags are compared with.</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Gets whether the text is a language tag, in any case.</summary>
    public static bool IsValid(string? text) => Canonicalize(text) is not null;

    /// <summary>Returns the tag in canonical case, such as <c>pt-BR</c> for <c>PT-br</c>, or null when the text is not a language tag.</summary>
    public static string? Canonicalize(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
            return null;

        var subtags = text.Split('-');
        if (subtags[0].Length is < 2 or > 3 || !subtags[0].All(char.IsAsciiLetter))
            return null;

        var canonical = new List<string>(subtags.Length) { subtags[0].ToLowerInvariant() };
        var next = 1;
        if (next < subtags.Length && IsScript(subtags[next]))
        {
            var script = subtags[next++];
            canonical.Add(char.ToUpperInvariant(script[0]) + script[1..].ToLowerInvariant());
        }

        if (next < subtags.Length && IsRegion(subtags[next]))
            canonical.Add(subtags[next++].ToUpperInvariant());
        for (; next < subtags.Length; next++)
        {
            if (!IsVariant(subtags[next]))
                return null;
            canonical.Add(subtags[next].ToLowerInvariant());
        }

        return string.Join('-', canonical);
    }

    private static bool IsScript(string subtag) => subtag.Length == 4 && subtag.All(char.IsAsciiLetter);

    private static bool IsRegion(string subtag) =>
        (subtag.Length == 2 && subtag.All(char.IsAsciiLetter)) || (subtag.Length == 3 && subtag.All(char.IsAsciiDigit));

    private static bool IsVariant(string subtag) =>
        subtag.All(char.IsAsciiLetterOrDigit) && (subtag.Length is >= 5 and <= 8 || (subtag.Length == 4 && char.IsAsciiDigit(subtag[0])));
}
