using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Tool.Index;

/// <summary>Writes files into an index folder with the layout clients read.</summary>
internal sealed class IndexFolder(string directory)
{
    public string Directory { get; } = directory;

    /// <summary>Writes a file at a path relative to the index root, such as <c>timestamp.json</c>.</summary>
    public string Write(string relativePath, byte[] bytes)
    {
        var path = Path.Combine(Directory, relativePath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Writes a file named by its own SHA-256 and returns how to pin it.</summary>
    /// <param name="pathFor">Gives the relative path for a hash, such as <see cref="IndexPaths.Snapshot"/>.</param>
    public FileMeta WritePinned(Func<string, string> pathFor, byte[] bytes, int version)
    {
        var meta = FileMeta.Of(version, bytes);
        Write(pathFor(meta.Sha256), bytes);
        return meta;
    }

    /// <summary>Writes a root envelope as <c>root/&lt;version&gt;.json</c>.</summary>
    public string WriteRoot(int version, byte[] envelope) => Write(IndexPaths.Root(version), envelope);

    /// <summary>Checks the folder is empty or missing, so nothing is overwritten.</summary>
    /// <exception cref="ToolException">The folder has files in it.</exception>
    public static void EnsureEmpty(string directory)
    {
        if (System.IO.Directory.Exists(directory) && System.IO.Directory.EnumerateFileSystemEntries(directory).Any())
            throw new ToolException($"{directory} is not empty.");
    }
}
