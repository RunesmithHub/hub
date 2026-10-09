using System.Xml.Linq;

namespace RunesmithHub.Tool.NuGet;

/// <summary>Writes the NuGet configuration hub builds restore with, replacing whatever the plugin's repository configures.</summary>
internal static class NuGetConfigWriter
{
    public const string FileName = "nuget.config";

    public static string Create(Uri hubFeed)
    {
        ArgumentNullException.ThrowIfNull(hubFeed);
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("configuration",
                new XElement("packageSources",
                    new XElement("clear"),
                    Source(NuGetFeeds.NuGetOrgKey, NuGetFeeds.NuGetOrg),
                    Source(NuGetFeeds.HubFeedKey, hubFeed.AbsoluteUri)),
                new XElement("packageSourceMapping",
                    new XElement("packageSource", new XAttribute("key", NuGetFeeds.NuGetOrgKey), Pattern("*")),
                    new XElement("packageSource", new XAttribute("key", NuGetFeeds.HubFeedKey), NuGetFeeds.HubPatterns.Select(Pattern)))));
        return document.Declaration + "\n" + document.Root!.ToString().Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static XElement Source(string key, string value) => new("add", new XAttribute("key", key), new XAttribute("value", value));

    private static XElement Pattern(string pattern) => new("package", new XAttribute("pattern", pattern));
}
