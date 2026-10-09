namespace RunesmithHub.Tool.NuGet;

/// <summary>
/// The two package sources hub builds use and the mapping between them: <c>Runesmith.*</c>, <c>RunesmithHub.*</c> and <c>HammerUI</c> come
/// only from the hub feed, everything else from nuget.org.
/// </summary>
internal static class NuGetFeeds
{
    /// <summary>The environment variable that overrides the hub feed's service index URL.</summary>
    public const string HubFeedVariable = "HUB_FEED_URL";

    public const string DefaultHubFeed = "https://nuget.runesmith.dev/index.json";

    public const string NuGetOrg = "https://api.nuget.org/v3/index.json";

    public const string NuGetOrgFlatContainer = "https://api.nuget.org/v3-flatcontainer/";

    public const string HubFeedKey = "runesmith-hub";

    public const string NuGetOrgKey = "nuget.org";

    /// <summary>Gets the package ID patterns mapped to the hub feed.</summary>
    public static IReadOnlyList<string> HubPatterns { get; } = ["Runesmith.*", "RunesmithHub.*", "HammerUI"];

    /// <summary>Gets whether a package comes from the hub feed rather than nuget.org.</summary>
    public static bool IsHubPackage(string id) =>
        HubPatterns.Any(pattern => pattern.EndsWith('*')
            ? id.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
            : id.Equals(pattern, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets whether a package is another plugin's contracts package, which a plugin never bundles.</summary>
    public static bool IsContractsPackage(string id) =>
        id.StartsWith("RunesmithHub.", StringComparison.OrdinalIgnoreCase) && id.EndsWith(".Contracts", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the hub feed's service index URL from an option, then the environment, then the default.</summary>
    public static Uri HubFeed(string? option, Func<string, string?> getEnvironmentVariable)
    {
        var text = option ?? (getEnvironmentVariable(HubFeedVariable) is { Length: > 0 } variable ? variable : DefaultHubFeed);
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new ToolException($"The hub feed URL \"{text}\" is not an https:// URL.");
    }
}
