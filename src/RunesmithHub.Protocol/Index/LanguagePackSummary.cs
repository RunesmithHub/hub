namespace RunesmithHub.Protocol.Index;

/// <summary>What a version of a language pack is: the language it adds, named in that language and in English, and what it translates.</summary>
public sealed record LanguagePackSummary
{
    /// <summary>The target of Runesmith's own messages; every other target is a plugin ID.</summary>
    public const string RunesmithTarget = "runesmith";

    /// <summary>Gets the language's BCP 47 tag, such as <c>nl</c>.</summary>
    public required string Language { get; init; }

    /// <summary>Gets the language's name in that language, such as <c>Nederlands</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the language's name in English, such as <c>Dutch</c>.</summary>
    public required string EnglishName { get; init; }

    // Source-generated JSON passes null for omitted init-only properties, so the setter restores the default.
    /// <summary>Gets what the pack translates: <see cref="RunesmithTarget"/> first when it translates Runesmith, then plugin IDs.</summary>
    public IReadOnlyList<string> Targets { get; init => field = value ?? []; } = [];
}
