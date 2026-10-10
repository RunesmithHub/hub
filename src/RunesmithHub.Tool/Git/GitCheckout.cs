using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Git;

/// <summary>The few <c>git</c> commands the build needs, run against the repository at a checkout's root folder.</summary>
internal sealed class GitCheckout(IProcessRunner processes)
{
    private const string GitlinkMode = "160000";

    /// <summary>Gets the full SHA of the checked out commit.</summary>
    public async Task<string> HeadCommitAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        (await RunAsync(repositoryRoot, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false)).Trim();

    /// <summary>Lists the paths of submodules in a commit's tree.</summary>
    public async Task<IReadOnlyList<string>> SubmodulesAsync(string repositoryRoot, string commit, CancellationToken cancellationToken = default)
    {
        var tree = await RunAsync(repositoryRoot, ["ls-tree", "-r", commit], cancellationToken).ConfigureAwait(false);
        return [.. tree.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(GitlinkMode + " ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf('\t', StringComparison.Ordinal) + 1)..])];
    }

    /// <summary>
    /// Writes the tree of a commit, read from the object database with the attributes committed in it, as an uncompressed tar file whose
    /// entries all start with a prefix folder.
    /// </summary>
    public Task ArchiveAsync(string repositoryRoot, string commit, string prefix, string tarFile, CancellationToken cancellationToken = default) =>
        RunAsync(repositoryRoot, ["archive", "--format=tar", $"--prefix={prefix}/", "-o", tarFile, commit], cancellationToken);

    private async Task<string> RunAsync(string repositoryRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAsync(processes, repositoryRoot, arguments, cancellationToken).ConfigureAwait(false);
        return result.OutputOrThrow($"git {arguments[0]}");
    }
}
