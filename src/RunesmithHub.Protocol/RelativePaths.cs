namespace RunesmithHub.Protocol;

/// <summary>Checks paths inside a plugin repository or package.</summary>
public static class RelativePaths
{
    /// <summary>
    /// Gets whether a path is printable ASCII, relative, uses forward slashes, and has no empty, <c>.</c> or <c>..</c> segments, no leading
    /// slash and no drive letter.
    /// </summary>
    public static bool IsSafe(string? path)
    {
        if (string.IsNullOrEmpty(path) || path[0] == '/' || path.Contains('\\', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal))
            return false;
        if (!path.All(c => c is >= ' ' and <= '~'))
            return false;
        return path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    /// <summary>Gets whether a path ends in one of the extensions, compared case-insensitively.</summary>
    public static bool HasExtension(string path, params ReadOnlySpan<string> extensions)
    {
        foreach (var extension in extensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
