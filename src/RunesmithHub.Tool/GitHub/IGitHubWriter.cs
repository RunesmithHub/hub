namespace RunesmithHub.Tool.GitHub;

/// <summary>A change to the registry that the hub bot proposes as a pull request.</summary>
/// <param name="Files">The files to write, by path from the repository root; a null value deletes the file.</param>
internal sealed record ChangeSet(string Branch, string Title, string Body, IReadOnlyDictionary<string, byte[]?> Files, IReadOnlyList<string> Labels, bool AutoMerge);

/// <summary>What the hub bot does on GitHub. The workflow mints the bot's installation token; the tool only uses it.</summary>
internal interface IGitHubWriter
{
    Task<int> CreateIssueAsync(string repository, string title, string body, IReadOnlyList<string> labels, CancellationToken cancellationToken);

    Task CommentAsync(string repository, int number, string body, CancellationToken cancellationToken);

    Task AddLabelsAsync(string repository, int number, IReadOnlyList<string> labels, CancellationToken cancellationToken);

    Task CloseIssueAsync(string repository, int number, CancellationToken cancellationToken);

    /// <summary>Commits the files to a new branch from the default branch and opens a pull request; returns its number.</summary>
    Task<int> OpenPullRequestAsync(string repository, ChangeSet changes, CancellationToken cancellationToken);

    /// <summary>Adds a commit with the files to an existing pull request's branch.</summary>
    Task UpdateBranchAsync(string repository, string branch, string message, IReadOnlyDictionary<string, byte[]?> files, CancellationToken cancellationToken);

    Task ClosePullRequestAsync(string repository, int number, CancellationToken cancellationToken);

    Task SetStatusAsync(string repository, string sha, string context, CommitStatusState state, string description, CancellationToken cancellationToken);

    /// <summary>Creates a release for a tag on the default branch's head, or returns the existing one.</summary>
    Task<ReleaseWithAssets> EnsureReleaseAsync(string repository, string tag, string name, string body, CancellationToken cancellationToken);

    Task UploadAssetAsync(ReleaseWithAssets release, string name, byte[] content, string contentType, CancellationToken cancellationToken);

    Task DeleteAssetAsync(string repository, long assetId, CancellationToken cancellationToken);

    /// <summary>Drafts a repository security advisory and returns its URL.</summary>
    Task<string> DraftAdvisoryAsync(string repository, string summary, string description, CancellationToken cancellationToken);
}
