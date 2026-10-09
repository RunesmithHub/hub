namespace RunesmithHub.Tool.Fixture;

/// <summary>Where to write the fixture and how.</summary>
/// <param name="BaseUrl">The URL the folder is served at, ending in <c>/</c>; package and image links start with it.</param>
/// <param name="Expired">Whether the timestamp has already expired.</param>
internal sealed record FixtureOptions(string Directory, Uri BaseUrl, bool Expired)
{
    public const string DefaultBaseUrl = "http://localhost:8080/";

    /// <exception cref="ToolException">The text is not an absolute HTTP or HTTPS URL ending in <c>/</c>.</exception>
    public static Uri ParseBaseUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) && url.AbsolutePath.EndsWith('/')
            ? url
            : throw new ToolException($"\"{text}\" is not an http:// or https:// URL ending in /.");
}
