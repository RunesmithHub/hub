namespace RunesmithHub.Tool.Registry;

/// <summary>The files of the registry repository at one point: a checked-out folder or a commit.</summary>
internal interface IRegistryFiles
{
    /// <summary>Gets a file's bytes by its path from the repository root, or null when it does not exist.</summary>
    Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>Lists every file under a folder, recursively, as paths from the repository root with forward slashes.</summary>
    Task<IReadOnlyList<string>> ListAsync(string directory, CancellationToken cancellationToken);
}
