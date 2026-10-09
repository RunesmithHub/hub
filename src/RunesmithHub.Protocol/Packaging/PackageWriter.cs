using System.IO.Compression;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Manifests;

namespace RunesmithHub.Protocol.Packaging;

/// <summary>
/// Writes packages deterministically: entries sorted ordinally, every timestamp 1980-01-01T00:00:00, no attributes or extra fields,
/// deflate compression. The same files always give the same bytes.
/// </summary>
public static class PackageWriter
{
    private static readonly DateTimeOffset FixedTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Writes a package and checks it against every rule before returning.</summary>
    /// <exception cref="PackageException">The files break a packaging rule.</exception>
    public static byte[] Write(IEnumerable<PackageFile> files, PackageLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        var sorted = files.OrderBy(file => file.Path, StringComparer.Ordinal).ToList();
        var declaresProcess = sorted.Any(file => PackageEntryRules.IsProcessFile(file.Path)) && DeclaresProcess(sorted);
        var problems = sorted.Select(file => PackageEntryRules.Check(file.Path, declaresProcess)).OfType<string>().ToList();
        if (problems.Count > 0)
            throw new PackageException(problems);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in sorted)
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                entry.LastWriteTime = FixedTime;
                entry.ExternalAttributes = 0;
                using var stream = entry.Open();
                stream.Write(file.Content.Span);
            }
        }

        buffer.Position = 0;
        using (PackageArchive.Open(buffer, limits))
        {
        }

        return buffer.ToArray();
    }

    private static bool DeclaresProcess(IEnumerable<PackageFile> files)
    {
        if (files.FirstOrDefault(file => file.Path == DeveloperManifest.FileName) is not { } manifest)
            return false;
        try
        {
            return PackageEntryRules.DeclaresProcess(IndexJson.Deserialize<PackagedManifest>(manifest.Content.Span));
        }
        catch (InvalidDataException exception)
        {
            throw new PackageException($"The packaged manifest is not valid: {exception.Message}", exception);
        }
    }
}
