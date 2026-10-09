using RunesmithHub.Protocol.Updating;

namespace RunesmithHub.Tool;

/// <summary>Turns an index location given on the command line into a fetcher.</summary>
internal static class IndexLocation
{
    /// <summary>Creates a fetcher for an <c>https://</c> base URL (a trailing slash is added) or for a local folder.</summary>
    public static IIndexFetcher CreateFetcher(string location, HttpClient http)
    {
        if (location.StartsWith("https://", StringComparison.Ordinal) || location.StartsWith("http://", StringComparison.Ordinal))
            return new HttpIndexFetcher(http, new Uri(location.EndsWith('/') ? location : location + "/"));
        return new DirectoryIndexFetcher(location);
    }
}
