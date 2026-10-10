namespace RunesmithHub.Protocol.Manifests;

/// <summary>Marks a plugin as a language pack, as the developer writes it: the one language it adds, named in that language and in English.</summary>
public sealed record ManifestLanguagePack
{
    /// <summary>Gets the language's BCP 47 tag, such as <c>nl</c>.</summary>
    public string? Language { get; init; }

    /// <summary>Gets the language's name in that language, such as <c>Nederlands</c>.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the language's name in English, such as <c>Dutch</c>.</summary>
    public string? EnglishName { get; init; }
}
