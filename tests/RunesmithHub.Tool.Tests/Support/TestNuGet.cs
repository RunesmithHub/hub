using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>Writes small NuGet packages for tests; the same arguments always give the same bytes.</summary>
internal static class TestNuGet
{
    private static readonly DateTimeOffset EntryTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static byte[] Package(string id, string version, string? license, params (string Path, byte[] Content)[] files)
    {
        var licenseElement = license is null ? "" : $"<license type=\"expression\">{license}</license>";
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, $"{id}.nuspec", Encoding.UTF8.GetBytes($"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{id}</id>
                    <version>{version}</version>
                    <authors>Someone</authors>
                    {licenseElement}
                    <projectUrl>https://example.com/{id}</projectUrl>
                    <copyright>Copyright Someone</copyright>
                  </metadata>
                </package>
                """));
            foreach (var (path, content) in files)
                Add(archive, path, content);
        }

        return buffer.ToArray();
    }

    /// <summary>Gets the content hash a lock file records for unsigned package bytes: their base64 SHA-512.</summary>
    public static string ContentHash(byte[] package) => Convert.ToBase64String(SHA512.HashData(package));

    /// <summary>Writes a lock file pinning the packages, all under one target framework.</summary>
    public static void WriteLockFile(string path, IEnumerable<(string Id, string Version, string ContentHash)> packages)
    {
        var entries = packages.Select(package =>
            $"\"{package.Id}\": {{ \"type\": \"Direct\", \"requested\": \"[{package.Version}, )\", \"resolved\": \"{package.Version}\", \"contentHash\": \"{package.ContentHash}\" }}");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"{{ \"version\": 1, \"dependencies\": {{ \"net10.0\": {{ {string.Join(", ", entries)} }} }} }}\n");
    }

    private static void Add(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path);
        entry.LastWriteTime = EntryTime;
        using var stream = entry.Open();
        stream.Write(content);
    }
}
