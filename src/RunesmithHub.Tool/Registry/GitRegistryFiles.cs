using RunesmithHub.Tool.Git;

namespace RunesmithHub.Tool.Registry;

/// <summary>The registry as it was at a commit.</summary>
internal sealed class GitRegistryFiles(IGitRepository git, string gitRef) : IRegistryFiles
{
    public Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken) => git.ShowAsync(gitRef, path, cancellationToken);

    public Task<IReadOnlyList<string>> ListAsync(string directory, CancellationToken cancellationToken) => git.ListAsync(gitRef, directory, cancellationToken);
}
