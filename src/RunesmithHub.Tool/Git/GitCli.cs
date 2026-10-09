using System.Text;
using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Git;

/// <summary>Reads a repository by running the <c>git</c> command line in its folder.</summary>
internal sealed class GitCli(IProcessRunner processes, string root) : IGitRepository
{
    public async Task<IReadOnlyList<ChangedPath>> DiffAsync(string fromRef, string toRef, CancellationToken cancellationToken)
    {
        var output = await RunAsync(["diff", "--name-status", "--no-renames", "-z", fromRef, toRef, "--"], cancellationToken).ConfigureAwait(false);
        var parts = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<ChangedPath>();
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var change = parts[i][0] switch
            {
                'A' => FileChange.Added,
                'D' => FileChange.Deleted,
                _ => FileChange.Modified,
            };
            changes.Add(new ChangedPath(parts[i + 1], change));
        }

        return changes;
    }

    public async Task<byte[]?> ShowAsync(string gitRef, string path, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("git", ["cat-file", "blob", $"{gitRef}:{path}"], root, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? Encoding.UTF8.GetBytes(result.Output) : null;
    }

    public async Task<IReadOnlyList<string>> ListAsync(string gitRef, string directory, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("git", ["ls-tree", "-r", "-z", "--name-only", gitRef, "--", directory.TrimEnd('/') + "/"], root, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries) : [];
    }

    public async Task<string?> CommitAddingAsync(string gitRef, string path, CancellationToken cancellationToken)
    {
        var output = await RunAsync(["log", gitRef, "--diff-filter=A", "--format=%H", "-1", "--", path], cancellationToken).ConfigureAwait(false);
        var sha = output.Trim();
        return sha.Length == 0 ? null : sha;
    }

    public async Task<string> MergeBaseAsync(string left, string right, CancellationToken cancellationToken) =>
        (await RunAsync(["merge-base", left, right], cancellationToken).ConfigureAwait(false)).Trim();

    private async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("git", arguments, root, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.Output : throw new ToolException($"git {arguments[0]} failed: {result.Error.Trim()}");
    }
}
