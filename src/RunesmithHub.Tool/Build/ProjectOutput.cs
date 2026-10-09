using System.Xml;
using System.Xml.Linq;
using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Build;

/// <summary>Where a project's Release build wrote its assembly, and what restore resolved for it.</summary>
internal sealed record ProjectOutput(string AssemblyName, string Directory, AssetsFile Assets)
{
    public const string Configuration = "Release";

    public string AssemblyFile => AssemblyName + ".dll";

    public string AssemblyPath => Path.Combine(Directory, AssemblyFile);

    /// <summary>Finds a built project's output in <c>bin/Release/&lt;framework&gt;/</c>.</summary>
    /// <exception cref="ToolException">The project was not restored and built, or its assembly name is computed.</exception>
    public static ProjectOutput Locate(string projectFile)
    {
        var assets = AssetsFile.Read(AssetsFile.PathFor(projectFile));
        var name = AssemblyNameOf(projectFile);
        var directory = Path.Combine(Path.GetDirectoryName(projectFile)!, "bin", Configuration, assets.TargetFramework);
        var output = new ProjectOutput(name, directory, assets);
        return File.Exists(output.AssemblyPath)
            ? output
            : throw new ToolException($"{output.AssemblyPath} does not exist; build {Path.GetFileName(projectFile)} in {Configuration} first.");
    }

    private static string AssemblyNameOf(string projectFile)
    {
        XDocument project;
        try
        {
            using var reader = XmlReader.Create(projectFile, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            project = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new ToolException($"{projectFile} is not valid XML: {exception.Message}", exception);
        }

        var declared = project.Descendants().LastOrDefault(element => element.Name.LocalName == "AssemblyName")?.Value.Trim();
        if (declared is null or "")
            return Path.GetFileNameWithoutExtension(projectFile);
        return declared.Contains("$(", StringComparison.Ordinal)
            ? throw new ToolException($"{Path.GetFileName(projectFile)} computes its AssemblyName; hub plugins set it as plain text.")
            : declared;
    }
}
