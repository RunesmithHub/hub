using System.IO.Compression;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Manifests;

namespace RunesmithHub.Protocol.Packaging;

/// <summary>
/// An opened <c>.rsplugin</c> package whose entries passed every packaging rule. Extraction counts the bytes it actually writes, so an
/// entry that lies about its size can't pass a limit.
/// </summary>
public sealed class PackageArchive : IDisposable
{
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymbolicLink = 0xA000;
    private const int WindowsReparsePoint = 0x400;
    private const long MaxManifestBytes = 1024 * 1024;

    private readonly ZipArchive _archive;
    private readonly PackageLimits _limits;
    private readonly IReadOnlyList<ZipArchiveEntry> _files;

    private PackageArchive(ZipArchive archive, PackageLimits limits, IReadOnlyList<ZipArchiveEntry> files)
    {
        _archive = archive;
        _limits = limits;
        _files = files;
    }

    /// <summary>Gets the names of the file entries, in archive order.</summary>
    public IReadOnlyList<string> EntryNames => [.. _files.Select(entry => entry.FullName)];

    /// <summary>Opens a package and checks every entry against the rules before anything is extracted.</summary>
    /// <param name="stream">A seekable stream with the package; it stays open after the archive is disposed.</param>
    /// <exception cref="PackageException">The package breaks a rule; every problem found is listed.</exception>
    public static PackageArchive Open(Stream stream, PackageLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        limits ??= PackageLimits.Default;
        if (stream.CanSeek && stream.Length > limits.MaxPackageBytes)
            throw new PackageException($"The package is {stream.Length} bytes; at most {limits.MaxPackageBytes} are allowed.");

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new PackageException("The package is not a valid ZIP archive.", exception);
        }

        try
        {
            var files = Check(archive, limits);
            return new PackageArchive(archive, limits, files);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    private static List<ZipArchiveEntry> Check(ZipArchive archive, PackageLimits limits)
    {
        var problems = new List<string>();
        var entries = archive.Entries;
        if (entries.Count > PackageLimits.MaxEntries)
            throw new PackageException($"The package has {entries.Count} entries; at most {PackageLimits.MaxEntries} are allowed.");

        var declaresProcess = entries.Any(entry => PackageEntryRules.IsProcessFile(entry.FullName)) && DeclaresProcess(entries, problems);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<ZipArchiveEntry>();
        long total = 0;
        foreach (var entry in entries)
        {
            var name = entry.FullName;
            if (IsSymbolicLink(entry))
                problems.Add($"\"{name}\" is a link.");
            if (!names.Add(name.TrimEnd('/')))
                problems.Add($"\"{name}\" appears more than once, ignoring case.");
            if (name.EndsWith('/'))
            {
                if (entry.Length != 0 || RelativePaths.IsSafe(name.TrimEnd('/')) is false)
                    problems.Add($"\"{name}\" is not a valid folder entry.");
                continue;
            }

            if (PackageEntryRules.Check(name, declaresProcess) is { } problem)
                problems.Add(problem);
            if (!IsRatioAllowed(entry.Length, entry.CompressedLength))
                problems.Add($"\"{name}\" compresses more than {PackageLimits.MaxCompressionRatio} to 1.");
            total += entry.Length;
            files.Add(entry);
        }

        if (total > limits.MaxUnpackedBytes)
            problems.Add($"The package unpacks to {total} bytes; at most {limits.MaxUnpackedBytes} are allowed.");
        if (!files.Any(entry => entry.FullName == DeveloperManifest.FileName))
            problems.Add($"The package has no {DeveloperManifest.FileName}.");
        if (problems.Count > 0)
            throw new PackageException(problems);
        return files;
    }

    private static bool DeclaresProcess(IEnumerable<ZipArchiveEntry> entries, List<string> problems)
    {
        if (entries.FirstOrDefault(entry => entry.FullName == DeveloperManifest.FileName) is not { } entry)
            return false;
        try
        {
            return PackageEntryRules.DeclaresProcess(ReadManifest(entry));
        }
        catch (PackageException exception)
        {
            problems.AddRange(exception.Problems);
            return false;
        }
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        var attributes = entry.ExternalAttributes;
        return ((attributes >> 16) & UnixFileTypeMask) == UnixSymbolicLink || (attributes & WindowsReparsePoint) != 0;
    }

    private static bool IsRatioAllowed(long length, long compressedLength) =>
        length == 0 || (compressedLength > 0 && length / (double)compressedLength <= PackageLimits.MaxCompressionRatio);

    /// <summary>Reads the packaged manifest.</summary>
    /// <exception cref="PackageException">The manifest is too large or not valid.</exception>
    public PackagedManifest ReadManifest() => ReadManifest(_files.First(file => file.FullName == DeveloperManifest.FileName));

    private static PackagedManifest ReadManifest(ZipArchiveEntry entry)
    {
        using var buffer = new MemoryStream();
        CopyLimited(entry, buffer, Math.Min(entry.Length, MaxManifestBytes));
        try
        {
            return IndexJson.Deserialize<PackagedManifest>(buffer.ToArray());
        }
        catch (InvalidDataException exception)
        {
            throw new PackageException($"The packaged manifest is not valid: {exception.Message}", exception);
        }
    }

    /// <summary>Extracts every file into an empty staging folder, which is created if needed.</summary>
    /// <exception cref="PackageException">An entry holds more bytes than it declares, or the total passes the limit.</exception>
    /// <exception cref="IOException">The folder is not empty.</exception>
    public void ExtractTo(string stagingDirectory)
    {
        var root = Path.GetFullPath(Directory.CreateDirectory(stagingDirectory).FullName);
        if (Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException($"{root} is not empty.");

        long total = 0;
        foreach (var entry in _files)
        {
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new PackageException($"\"{entry.FullName}\" points outside the staging folder.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            total += CopyLimited(entry, output, entry.Length);
            if (total > _limits.MaxUnpackedBytes)
                throw new PackageException($"The package unpacks to more than {_limits.MaxUnpackedBytes} bytes.");
        }
    }

    private static long CopyLimited(ZipArchiveEntry entry, Stream output, long limit)
    {
        try
        {
            using var input = entry.Open();
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                written += read;
                if (written > limit)
                    throw new PackageException($"\"{entry.FullName}\" holds more bytes than it declares.");
                output.Write(buffer, 0, read);
            }

            return written;
        }
        catch (InvalidDataException exception)
        {
            throw new PackageException($"\"{entry.FullName}\" is damaged: {exception.Message}", exception);
        }
    }

    public void Dispose() => _archive.Dispose();
}
