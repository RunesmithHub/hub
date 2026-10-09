using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using NuGet.Packaging;
using NuGet.Packaging.Signing;

namespace RunesmithHub.Tool.NuGet;

/// <summary>How a package states its license: an SPDX expression, a license file inside it, or a URL.</summary>
internal sealed record PackageLicense(string? Expression, string? FileText, string? Url);

/// <summary>A downloaded package file, opened read-only.</summary>
internal sealed class NuGetPackage : IDisposable
{
    private const long MaxEntryBytes = 128L * 1024 * 1024;

    private readonly ZipArchive _archive;

    private NuGetPackage(byte[] bytes, ZipArchive archive)
    {
        ContentHash = ContentHashOf(bytes);
        _archive = archive;
        Nuspec = ReadNuspec(archive);
    }

    /// <summary>Gets NuGet's content hash, as lock files record it: the base64 SHA-512 of the package without its signature.</summary>
    public string ContentHash { get; }

    private XElement Nuspec { get; }

    /// <exception cref="ToolException">The bytes are not a package.</exception>
    public static NuGetPackage Open(byte[] bytes)
    {
        try
        {
            return new NuGetPackage(bytes, new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read));
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or SignatureException)
        {
            throw new ToolException($"Not a valid NuGet package: {exception.Message}", exception);
        }
    }

    /// <summary>Reads a file inside the package, or returns null when there is none at that path.</summary>
    public byte[]? ReadFile(string path)
    {
        var entry = _archive.Entries.FirstOrDefault(entry => string.Equals(entry.FullName, path, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return null;
        if (entry.Length > MaxEntryBytes)
            throw new ToolException($"{path} in the package is larger than {MaxEntryBytes} bytes.");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Gets whether some file in the package with the given file name, in any folder, has exactly the given bytes.</summary>
    public bool HasFile(string fileName, ReadOnlySpan<byte> content)
    {
        foreach (var path in _archive.Entries.Select(entry => entry.FullName).Where(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase)))
        {
            if (ReadFile(path) is { } bytes && bytes.AsSpan().SequenceEqual(content))
                return true;
        }

        return false;
    }

    public string? Copyright => Metadata("copyright");

    public string? ProjectUrl => Metadata("projectUrl");

    public string? Authors => Metadata("authors");

    public PackageLicense License
    {
        get
        {
            var license = Nuspec.Elements().FirstOrDefault(element => element.Name.LocalName == "license");
            var type = license?.Attribute("type")?.Value;
            var text = license?.Value.Trim();
            var fileText = type == "file" && text is not null && ReadFile(text) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;
            return new PackageLicense(type == "expression" ? text : null, fileText, Metadata("licenseUrl"));
        }
    }

    private string? Metadata(string name) =>
        Nuspec.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value ? value : null;

    private static string ContentHashOf(byte[] bytes)
    {
        using var reader = new PackageArchiveReader(new MemoryStream(bytes, writable: false));
        return reader.GetContentHash(CancellationToken.None);
    }

    private static XElement ReadNuspec(ZipArchive archive)
    {
        var entry = archive.Entries.FirstOrDefault(entry => !entry.FullName.Contains('/', StringComparison.Ordinal) && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The package has no .nuspec file.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader);
        return document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "metadata") ?? throw new InvalidDataException("The .nuspec file has no metadata.");
    }

    public void Dispose() => _archive.Dispose();
}
