using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Git;

namespace RunesmithHub.Tool.Registry;

/// <summary>Answers questions about the registry's past.</summary>
internal interface IRegistryHistory
{
    /// <summary>Gets the maintainer IDs a plugin had when a version's record was added, or null when that is unknown.</summary>
    Task<IReadOnlySet<long>?> MaintainersAtVersionAsync(string pluginId, SemanticVersion version, CancellationToken cancellationToken);
}

/// <summary>Reads the registry's past from its Git history, up to a commit.</summary>
internal sealed class GitRegistryHistory(IGitRepository git, string gitRef) : IRegistryHistory
{
    public async Task<IReadOnlySet<long>?> MaintainersAtVersionAsync(string pluginId, SemanticVersion version, CancellationToken cancellationToken)
    {
        var commit = await git.CommitAddingAsync(gitRef, RegistryPaths.Version(pluginId, version), cancellationToken).ConfigureAwait(false);
        if (commit is null)
            return null;
        var path = RegistryPaths.Registration(pluginId);
        var bytes = await git.ShowAsync(commit, path, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : RegistryReader.Parse<Registration>(bytes, path).Maintainers.Select(maintainer => maintainer.Id).ToHashSet();
    }
}
