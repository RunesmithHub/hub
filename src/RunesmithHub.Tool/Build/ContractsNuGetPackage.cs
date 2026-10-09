using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Build;

/// <summary>
/// The NuGet package the hub feed serves with a version's contracts assembly, so dependents build against exactly what Runesmith loads.
/// It is written deterministically: the same contracts give the same bytes.
/// </summary>
internal static class ContractsNuGetPackage
{
    private const string NuspecNamespace = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
    private const string ContentTypesEntry = "[Content_Types].xml";
    private const int MaxEntries = 8;
    private static readonly DateTimeOffset FixedTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Gets the package ID for a contracts assembly, such as <c>RunesmithHub.Lumen.Todo.Contracts</c>.</summary>
    public static string Id(string contractsAssemblyFile) => "RunesmithHub." + Path.GetFileNameWithoutExtension(contractsAssemblyFile);

    public static string FileName(string id, string version) => $"{id}.{version}.nupkg";

    /// <summary>Writes the package with the contracts assembly, its documentation when there is one, and the hub packages it was built against.</summary>
    /// <param name="hubPackages">The <c>Runesmith.*</c> and <c>RunesmithHub.*</c> packages the contracts project's lock file pins.</param>
    public static byte[] Create(PackagedManifest manifest, ProjectOutput contracts, IEnumerable<LockedPackage> hubPackages)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(contracts);
        var id = Id(contracts.AssemblyFile);
        var lib = $"lib/{contracts.Assets.TargetFramework}/";
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ContentTypesEntry] = Encoding.UTF8.GetBytes(ContentTypes()),
            [id + ".nuspec"] = Encoding.UTF8.GetBytes(Nuspec(manifest, id, contracts.Assets.TargetFramework, hubPackages)),
            [lib + contracts.AssemblyFile] = File.ReadAllBytes(contracts.AssemblyPath),
        };
        var documentation = Path.ChangeExtension(contracts.AssemblyPath, ".xml");
        if (File.Exists(documentation))
            entries[lib + Path.GetFileName(documentation)] = File.ReadAllBytes(documentation);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                entry.LastWriteTime = FixedTime;
                entry.ExternalAttributes = 0;
                using var stream = entry.Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Lists how an untrusted contracts package differs from what the packaged manifest says it holds; empty when it matches.</summary>
    public static IReadOnlyList<string> Check(byte[] package, PackagedManifest manifest, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(manifest);
        var contracts = manifest.Assemblies.Contracts;
        var id = Id(contracts.File);
        var problems = new List<string>();
        try
        {
            using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxEntries)
                return [$"it has {archive.Entries.Count} entries"];

            var assemblies = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.Length > maxBytes)
                    return [$"{entry.FullName} is larger than the package limit"];
                var name = entry.FullName;
                if (name.StartsWith("lib/", StringComparison.Ordinal) && name.Count(c => c == '/') == 2 && name.EndsWith("/" + contracts.File, StringComparison.Ordinal))
                {
                    assemblies++;
                    if (!Hashes.Matches(Read(entry), contracts.Sha256))
                        problems.Add($"{name} is not the contracts assembly of the package");
                }
                else if (name == id + ".nuspec")
                {
                    problems.AddRange(CheckNuspec(Read(entry), id, manifest.Version.ToString()));
                }
                else if (name != ContentTypesEntry && !(name.StartsWith("lib/", StringComparison.Ordinal) && name.EndsWith("/" + Path.ChangeExtension(contracts.File, ".xml"), StringComparison.Ordinal)))
                {
                    problems.Add($"{name} does not belong in a contracts package");
                }
            }

            if (assemblies != 1)
                problems.Add($"it must hold {contracts.File} once");
            if (archive.GetEntry(id + ".nuspec") is null)
                problems.Add($"{id}.nuspec is missing");
        }
        catch (InvalidDataException exception)
        {
            problems.Add($"it is not a valid package: {exception.Message}");
        }

        return problems;
    }

    private static List<string> CheckNuspec(byte[] nuspec, string id, string version)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(nuspec), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            return [$"the nuspec is not valid XML: {exception.Message}"];
        }

        var metadata = document.Root?.Element(XName.Get("metadata", NuspecNamespace));
        var problems = new List<string>();
        if (metadata?.Element(XName.Get("id", NuspecNamespace))?.Value != id)
            problems.Add($"the nuspec does not name the package {id}");
        if (metadata?.Element(XName.Get("version", NuspecNamespace))?.Value != version)
            problems.Add($"the nuspec does not have the version {version}");
        return problems;
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static string Nuspec(PackagedManifest manifest, string id, string targetFramework, IEnumerable<LockedPackage> hubPackages)
    {
        XNamespace ns = NuspecNamespace;
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "package",
                new XElement(ns + "metadata",
                    new XElement(ns + "id", id),
                    new XElement(ns + "version", manifest.Version.ToString()),
                    new XElement(ns + "authors", string.Join(", ", manifest.Authors)),
                    new XElement(ns + "description", $"The contracts of {manifest.Name} ({manifest.Id}) for plugins that depend on it."),
                    new XElement(ns + "license", new XAttribute("type", "expression"), manifest.License),
                    new XElement(ns + "projectUrl", $"{HubLocations.PluginPage(manifest.Id)}"),
                    new XElement(ns + "repository", new XAttribute("type", "git"), new XAttribute("url", manifest.Repository), new XAttribute("commit", manifest.Build.Commit)),
                    new XElement(ns + "dependencies",
                        new XElement(ns + "group", new XAttribute("targetFramework", targetFramework),
                            hubPackages.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase).Select(package => new XElement(ns + "dependency",
                                new XAttribute("id", package.Id), new XAttribute("version", package.Version), new XAttribute("exclude", "Build,Analyzers"))))))));
        return document.Declaration + "\n" + document.Root!.ToString().Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static string ContentTypes() =>
        """<?xml version="1.0" encoding="utf-8"?>""" + "\n" +
        """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="nuspec" ContentType="application/octet" />""" +
        """<Default Extension="dll" ContentType="application/octet" /><Default Extension="xml" ContentType="application/octet" /></Types>""" + "\n";
}
