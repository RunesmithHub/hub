namespace RunesmithHub.Protocol.Index;

/// <summary>The language a language pack declares it adds, named in that language and in English.</summary>
public sealed record DeclaredLanguagePack
{
    /// <summary>Gets the language's BCP 47 tag, such as <c>nl</c>.</summary>
    public required string Language { get; init; }

    /// <summary>Gets the language's name in that language, such as <c>Nederlands</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the language's name in English, such as <c>Dutch</c>.</summary>
    public required string EnglishName { get; init; }
}
