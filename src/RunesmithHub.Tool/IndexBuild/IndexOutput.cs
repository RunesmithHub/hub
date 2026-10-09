using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Tool.IndexBuild;

/// <summary>Writes an index directory: every root from the registry and every file of the state, removing index files the state dropped.</summary>
internal static class IndexOutput
{
    private static readonly string[] Folders = ["root", "snapshot", "moderation", "plugins", "publishers", "policy"];
    private static readonly string[] TopFiles = [IndexPaths.Timestamp, IndexPaths.Dashboard, IndexPaths.Stats];

    public static void Write(string directory, TrustRoot trust, IndexState state)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(state);
        var files = new Dictionary<string, byte[]>(state.Files, StringComparer.Ordinal);
        for (var i = 0; i < trust.Envelopes.Count; i++)
            files[IndexPaths.Root(i + 1)] = trust.Envelopes[i];

        foreach (var stale in ManagedFiles(directory).Where(path => !files.ContainsKey(path)))
            File.Delete(Path.Combine(directory, stale));
        foreach (var (path, bytes) in files)
            WriteFile(directory, path, bytes);
    }

    /// <summary>Lists the index files in a directory, relative and with forward slashes; other files, such as a checkout's own, are left alone.</summary>
    public static IEnumerable<string> ManagedFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return [];
        var top = TopFiles.Where(name => File.Exists(Path.Combine(directory, name)));
        var nested = Folders.Select(folder => Path.Combine(directory, folder)).Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'));
        return [.. top.Concat(nested).Order(StringComparer.Ordinal)];
    }

    private static void WriteFile(string directory, string relativePath, byte[] bytes)
    {
        if (!Protocol.RelativePaths.IsSafe(relativePath))
            throw new ToolException($"Refusing to write the unsafe index path {relativePath}.");
        var path = Path.Combine(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
