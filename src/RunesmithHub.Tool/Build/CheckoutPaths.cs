namespace RunesmithHub.Tool.Build;

/// <summary>
/// Guards reads of a checked out repository and of what a build wrote into it, whose symbolic links could point anywhere on the machine:
/// the hub reads only regular files reached through regular folders inside the expected root.
/// </summary>
internal static class CheckoutPaths
{
    /// <summary>Checks that no folder or file on the way from a root to a path inside it is a symbolic link.</summary>
    /// <exception cref="ToolException">The path is outside the root or goes through a symbolic link.</exception>
    public static void EnsureNoLinks(string root, string path)
    {
        root = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        if (relative == ".")
            return;
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar)[0] == "..")
            throw new ToolException($"{path} is outside {root}.");

        var current = root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            var entry = new FileInfo(current);
            if (entry.LinkTarget is not null || ((entry.Exists || Directory.Exists(current)) && entry.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw new ToolException($"{Path.GetRelativePath(root, current).Replace('\\', '/')} is a symbolic link; the hub reads only regular files and folders.");
        }
    }

    /// <summary>Gets whether a regular file exists at a path inside a root.</summary>
    /// <exception cref="ToolException">The path is outside the root or goes through a symbolic link.</exception>
    public static bool FileExists(string root, string path)
    {
        EnsureNoLinks(root, path);
        return File.Exists(path);
    }

    /// <summary>Reads a regular file inside a root.</summary>
    /// <exception cref="ToolException">The file is missing, outside the root, or reached through a symbolic link.</exception>
    public static byte[] ReadFile(string root, string path)
    {
        if (!FileExists(root, path))
            throw new ToolException($"{path} does not exist.");
        return new FileInfo(path).Length == 0 ? [] : File.ReadAllBytes(path);
    }
}
