using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Tool.Versions;

/// <summary>The names and messages that tie a version to its tracking issue and its pull request.</summary>
internal static partial class TrackingIssues
{
    /// <summary>Gets a tracking issue's title, such as <c>lumen.todo 1.4.0</c>.</summary>
    public static string Title(string pluginId, SemanticVersion version) => $"{pluginId} {version}";

    /// <summary>Reads a tracking issue's title.</summary>
    public static bool TryParseTitle(string title, [NotNullWhen(true)] out string? pluginId, [NotNullWhen(true)] out SemanticVersion? version)
    {
        pluginId = null;
        version = null;
        var parts = title.Split(' ');
        if (parts.Length != 2 || !PluginId.IsValid(parts[0]) || !SemanticVersion.TryParse(parts[1], out version))
            return false;
        pluginId = parts[0];
        return true;
    }

    /// <summary>Gets the branch of a version's pull request.</summary>
    public static string Branch(string pluginId, SemanticVersion version) => $"version/{pluginId}/{version}";

    public static string PullRequestTitle(string pluginId, SemanticVersion version) => $"Publish {pluginId} {version}";

    /// <summary>Writes the opening text of a tracking issue, mentioning the maintainers.</summary>
    public static string Body(string pluginId, SemanticVersion version, string repository, string tag, IEnumerable<GitHubAccount> maintainers) =>
        new StringBuilder()
            .Append(CultureInfoInvariant($"The hub found the release [{tag}](https://github.com/{repository}/releases/tag/{Uri.EscapeDataString(tag)}) of `{pluginId}` "))
            .Append(CultureInfoInvariant($"and is building version {version}. Each step reports here.\n\n"))
            .Append("Maintainers: ").AppendJoin(", ", maintainers.Select(maintainer => "@" + maintainer.Login)).Append('\n')
            .ToString();

    /// <summary>Writes the comment that rejects a version; the dashboard reads the reason back from it.</summary>
    public static string Rejection(string reason, IEnumerable<string> details)
    {
        var text = new StringBuilder().Append("<!-- rejected: ").Append(reason.ReplaceLineEndings(" ").Replace("--", "-", StringComparison.Ordinal)).Append(" -->\n")
            .Append("**Rejected:** ").Append(reason).Append('\n');
        var list = details.ToList();
        if (list.Count > 0)
            text.Append('\n').AppendJoin('\n', list.Select(detail => "- " + detail.ReplaceLineEndings(" "))).Append('\n');
        return text.Append("\nFix the problem and release a new version number.\n").ToString();
    }

    /// <summary>Finds the reason in a rejection comment.</summary>
    public static string? RejectionReason(string comment) => RejectionPattern().Match(comment) is { Success: true } match ? match.Groups[1].Value : null;

    private static string CultureInfoInvariant(FormattableString text) => text.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex("<!-- rejected: (.*?) -->")]
    private static partial Regex RejectionPattern();
}
