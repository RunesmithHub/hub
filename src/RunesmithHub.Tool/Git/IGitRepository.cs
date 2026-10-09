namespace RunesmithHub.Tool.Git;

/// <summary>How a file changed between two commits.</summary>
internal enum FileChange
{
    Added,
    Modified,
    Deleted,
}

/// <summary>A file that differs between two commits.</summary>
internal sealed record ChangedPath(string Path, FileChange Change);

/// <summary>Reads a local Git repository's history.</summary>
internal interface IGitRepository
{
    /// <summary>Lists the files that differ between two commits; renames count as a deletion and an addition.</summary>
    Task<IReadOnlyList<ChangedPath>> DiffAsync(string fromRef, string toRef, CancellationToken cancellationToken);

    /// <summary>Gets a file's bytes at a commit, or null when it does not exist there.</summary>
    Task<byte[]?> ShowAsync(string gitRef, string path, CancellationToken cancellationToken);

    /// <summary>Lists the files under a folder at a commit, as paths relative to the repository root.</summary>
    Task<IReadOnlyList<string>> ListAsync(string gitRef, string directory, CancellationToken cancellationToken);

    /// <summary>Gets the newest commit reachable from <paramref name="gitRef"/> that added the file, or null.</summary>
    Task<string?> CommitAddingAsync(string gitRef, string path, CancellationToken cancellationToken);

    /// <summary>Gets the best common ancestor of two commits.</summary>
    Task<string> MergeBaseAsync(string left, string right, CancellationToken cancellationToken);
}
