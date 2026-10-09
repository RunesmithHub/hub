namespace RunesmithHub.Tool.Build;

/// <summary>Guards reads of a checked out repository, whose symbolic links could point anywhere on the machine.</summary>
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
            if (new FileInfo(current).LinkTarget is not null)
                throw new ToolException($"{Path.GetRelativePath(root, current).Replace('\\', '/')} is a symbolic link; the hub reads only regular files and folders.");
        }
    }
}
