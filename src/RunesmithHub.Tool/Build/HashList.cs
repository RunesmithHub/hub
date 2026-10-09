using RunesmithHub.Protocol;
using RunesmithHub.Tool.Json;

namespace RunesmithHub.Tool.Build;

/// <summary>One output file's path relative to the output folder, with its SHA-256 and length.</summary>
internal sealed record HashedFile(string Path, string Sha256, long Length);

/// <summary><c>hashes.json</c>: every file a build produced, so two independent builds can be compared and the publish job can check them.</summary>
internal sealed record HashList(IReadOnlyList<HashedFile> Files)
{
    public const string FileName = "hashes.json";

    /// <summary>Hashes every file in a folder except the hash list itself, sorted by path.</summary>
    public static HashList Create(string directory)
    {
        var root = Path.GetFullPath(directory);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => (File: file, Relative: Path.GetRelativePath(root, file).Replace('\\', '/')))
            .Where(file => file.Relative != FileName)
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .Select(file =>
            {
                using var stream = File.OpenRead(file.File);
                return new HashedFile(file.Relative, Hashes.Sha256(stream), stream.Length);
            });
        return new HashList([.. files]);
    }

    /// <exception cref="InvalidDataException">The file is not a hash list.</exception>
    public static HashList Read(string path) => ArtifactJson.Deserialize<HashList>(File.ReadAllBytes(path));

    public void Write(string directory) => File.WriteAllBytes(Path.Combine(directory, FileName), ArtifactJson.Serialize(this));

    /// <summary>Lists every path whose hash or length differs, or that only one list has, sorted.</summary>
    public IReadOnlyList<string> Differences(HashList other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var mine = Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var theirs = other.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        return [.. mine.Keys.Union(theirs.Keys, StringComparer.Ordinal)
            .Where(path => !mine.TryGetValue(path, out var left) || !theirs.TryGetValue(path, out var right) || left != right)
            .Order(StringComparer.Ordinal)];
    }
}
