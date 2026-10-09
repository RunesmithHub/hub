using System.Diagnostics.CodeAnalysis;

namespace RunesmithHub.Protocol;

/// <summary>
/// A publisher's name, such as <c>harbor</c>: a lowercase ASCII letter, then 1 to 39 lowercase letters, digits or single hyphens, not ending
/// in a hyphen. Plugin names follow the same rules.
/// </summary>
public readonly record struct PublisherName
{
    private const int MaxLength = 40;

    private PublisherName(string value) => Value = value;

    public string Value { get; }

    /// <exception cref="FormatException">The text is not a valid name.</exception>
    public static PublisherName Parse(string text) =>
        TryParse(text, out var name) ? name : throw new FormatException($"\"{text}\" is not a valid publisher name.");

    public static bool TryParse([NotNullWhen(true)] string? text, out PublisherName name)
    {
        name = default;
        if (!IsValid(text))
            return false;
        name = new PublisherName(text);
        return true;
    }

    /// <summary>Gets whether the text follows the naming rules.</summary>
    public static bool IsValid([NotNullWhen(true)] string? text)
    {
        if (text is null || text.Length is < 2 or > MaxLength || !char.IsAsciiLetterLower(text[0]) || text[^1] == '-')
            return false;
        if (text.Contains("--", StringComparison.Ordinal))
            return false;
        return text.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
    }

    public override string ToString() => Value ?? "";
}
