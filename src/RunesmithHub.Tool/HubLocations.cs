using System.Globalization;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Tool;

/// <summary>The hub's GitHub organization, repositories, teams and public addresses.</summary>
internal static class HubLocations
{
    public const string Organization = "RunesmithHub";
    public const string Registry = "RunesmithHub/registry";
    public const string Packages = "RunesmithHub/packages";
    public const string AdminsTeam = "admins";
    public const string OwnersTeam = "owners";
    public const string Website = "https://hub.runesmith.dev";

    /// <summary>Gets the tag of a version's release in the packages repository, such as <c>lumen.todo@1.4.0</c>.</summary>
    public static string ReleaseTag(string pluginId, SemanticVersion version) => $"{pluginId}@{version}";

    /// <summary>Gets the download URL of a file in a version's release in the packages repository.</summary>
    public static string ReleaseAssetUrl(string pluginId, SemanticVersion version, string file) =>
        $"https://github.com/{Packages}/releases/download/{Uri.EscapeDataString(ReleaseTag(pluginId, version))}/{Uri.EscapeDataString(file)}";

    public static string RunUrl(long runId) => string.Create(CultureInfo.InvariantCulture, $"https://github.com/{Registry}/actions/runs/{runId}");

    public static string IssueUrl(int number) => string.Create(CultureInfo.InvariantCulture, $"https://github.com/{Registry}/issues/{number}");

    public static string PluginPage(string pluginId) => $"{Website}/plugins/{pluginId}";
}

/// <summary>The labels the pipeline puts on issues and pull requests.</summary>
internal static class HubLabels
{
    public const string Tracking = "tracking";
    public const string Version = "version";
    public const string Rejected = "rejected";
    public const string Moderation = "moderation";
    public const string StrictPath = "strict-path";
    public const string Audit = "audit";
    public const string Developer = "developer";
    public const string Verification = "verification";
    public const string Broken = "broken-plugin";
    public const string Policy = "policy";
}
