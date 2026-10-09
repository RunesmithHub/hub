using System.Text;

namespace RunesmithHub.Tool.Build;

/// <summary>Writes <c>THIRD-PARTY-NOTICES.md</c> from the licenses of the NuGet packages a plugin bundles.</summary>
internal static class ThirdPartyNotices
{
    public const string FileName = "THIRD-PARTY-NOTICES.md";

    public static string Create(string pluginName, IReadOnlyList<BundledPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var text = new StringBuilder("# Third-party notices\n\n");
        if (packages.Count == 0)
            return text.Append(pluginName).Append(" bundles no third-party packages.\n").ToString();

        text.Append(pluginName).Append(" bundles the packages below. Each is used under its own license.\n");
        foreach (var package in packages)
        {
            text.Append("\n## ").Append(package.Id).Append(' ').Append(package.Version).Append("\n\n");
            text.Append("- License: ").Append(package.License.Expression ?? package.License.Url ?? (package.License.FileText is null ? "not stated" : "included below")).Append('\n');
            if (package.ProjectUrl is { } project)
                text.Append("- Project: ").Append(project).Append('\n');
            if (package.Copyright is { } copyright)
                text.Append("- Copyright: ").Append(copyright).Append('\n');
            if (package.License.FileText is { } license)
                text.Append("\n```text\n").Append(license.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd()).Append("\n```\n");
        }

        return text.ToString();
    }
}
