using System.Xml;
using System.Xml.Linq;

namespace RunesmithHub.Tool.Analysis;

/// <summary>Something in a project or MSBuild file that runs during the build beyond compiling.</summary>
/// <param name="Kind"><c>target</c>, <c>exec</c> or <c>task</c>.</param>
internal sealed record BuildLogicFinding(string File, int Line, string Kind, string Name);

/// <summary>Finds custom targets, <c>Exec</c> tasks and task declarations, inline or not, in the MSBuild files of a source tree.</summary>
internal static class BuildLogicScanner
{
    private static readonly string[] Extensions = [".csproj", ".props", ".targets", ".proj"];
    private static readonly string[] SkippedFolders = ["bin", "obj", ".git"];

    public static IReadOnlyList<BuildLogicFinding> Scan(string root)
    {
        var findings = new List<BuildLogicFinding>();
        foreach (var file in Files(Path.GetFullPath(root)))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            XDocument document;
            try
            {
                using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                document = XDocument.Load(reader, LoadOptions.SetLineInfo);
            }
            catch (XmlException)
            {
                findings.Add(new BuildLogicFinding(relative, 0, "unreadable", "the file is not valid XML"));
                continue;
            }

            foreach (var element in document.Descendants())
            {
                var kind = element.Name.LocalName switch
                {
                    "Target" => "target",
                    "Exec" => "exec",
                    "UsingTask" => "task",
                    _ => null,
                };
                if (kind is not null)
                    findings.Add(new BuildLogicFinding(relative, ((IXmlLineInfo)element).LineNumber, kind, NameOf(element)));
            }
        }

        return findings;
    }

    private static IEnumerable<string> Files(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory).Where(file => Extensions.Any(extension => file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) && new FileInfo(file).LinkTarget is null).Order(StringComparer.Ordinal))
            yield return file;
        foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
        {
            var info = new DirectoryInfo(child);
            if (SkippedFolders.Contains(info.Name, StringComparer.OrdinalIgnoreCase) || info.LinkTarget is not null)
                continue;
            foreach (var file in Files(child))
                yield return file;
        }
    }

    private static string NameOf(XElement element) => element.Name.LocalName switch
    {
        "Target" => element.Attribute("Name")?.Value ?? "",
        "UsingTask" => (element.Attribute("TaskName")?.Value ?? "") + (element.Elements().Any(child => child.Name.LocalName == "Task") ? " (inline)" : ""),
        _ => element.Ancestors().FirstOrDefault(ancestor => ancestor.Name.LocalName == "Target")?.Attribute("Name")?.Value is { } target ? $"in target {target}" : "",
    };
}
