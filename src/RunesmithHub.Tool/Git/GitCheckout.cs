using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Git;

/// <summary>The few <c>git</c> commands the build needs, run against a checkout.</summary>
internal sealed class GitCheckout(IProcessRunner processes)
{
    private const string GitlinkMode = "160000";

    /// <summary>Gets the full SHA of the checked out commit.</summary>
    public async Task<string> HeadCommitAsync(string directory, CancellationToken cancellationToken = default) =>
        (await RunAsync(directory, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false)).Trim();

    /// <summary>Gets the root folder of the repository a folder belongs to.</summary>
    public async Task<string> TopLevelAsync(string directory, CancellationToken cancellationToken = default) =>
        Path.GetFullPath((await RunAsync(directory, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false)).Trim());

    /// <summary>Gets whether a folder is inside a Git work tree.</summary>
    public async Task<bool> IsRepositoryAsync(string directory, CancellationToken cancellationToken = default) =>
        (await processes.RunAsync("git", ["rev-parse", "--is-inside-work-tree"], directory, cancellationToken).ConfigureAwait(false)).Succeeded;

    /// <summary>Lists the paths of submodules in a commit's tree.</summary>
    public async Task<IReadOnlyList<string>> SubmodulesAsync(string directory, string commit, CancellationToken cancellationToken = default)
    {
        var tree = await RunAsync(directory, ["ls-tree", "-r", commit], cancellationToken).ConfigureAwait(false);
        return [.. tree.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(GitlinkMode + " ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf('\t', StringComparison.Ordinal) + 1)..])];
    }

    /// <summary>Writes the tree of a commit as an uncompressed tar file whose entries all start with a prefix folder.</summary>
    public Task ArchiveAsync(string directory, string commit, string prefix, string tarFile, CancellationToken cancellationToken = default) =>
        RunAsync(directory, ["archive", "--format=tar", $"--prefix={prefix}/", "-o", tarFile, commit], cancellationToken);

    private async Task<string> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("git", arguments, directory, cancellationToken).ConfigureAwait(false);
        return result.OutputOrThrow($"git {arguments[0]}");
    }
}
