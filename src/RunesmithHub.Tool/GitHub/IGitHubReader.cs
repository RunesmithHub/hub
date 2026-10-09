using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Tool.GitHub;

/// <summary>Reads repositories, accounts, releases and files.</summary>
internal interface IRepositoryReader
{
    Task<GitHubRepository?> GetRepositoryAsync(long id, CancellationToken cancellationToken);

    Task<GitHubUser?> GetUserAsync(long id, CancellationToken cancellationToken);

    Task<GitHubUser?> GetUserAsync(string login, CancellationToken cancellationToken);

    /// <summary>Gets a file's bytes at a ref (the default branch when null), or null when it does not exist.</summary>
    Task<byte[]?> GetFileAsync(string repository, string path, string? gitRef, CancellationToken cancellationToken);

    /// <summary>Gets someone's permission on a repository; <see cref="RepositoryPermission.Unknown"/> when the token may not ask.</summary>
    Task<RepositoryPermission> GetPermissionAsync(string repository, string login, CancellationToken cancellationToken);

    /// <summary>Lists a repository's releases, newest first.</summary>
    Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(string repository, CancellationToken cancellationToken);

    /// <summary>Resolves a tag to the commit it points at, following annotated tags, or null when the tag does not exist.</summary>
    Task<string?> ResolveTagAsync(string repository, string tag, CancellationToken cancellationToken);

    Task<ReleaseWithAssets?> GetReleaseByTagAsync(string repository, string tag, CancellationToken cancellationToken);

    Task<BranchRules> GetBranchRulesAsync(string repository, string branch, CancellationToken cancellationToken);

    /// <summary>Gets whether private vulnerability reporting is on, or null when the token may not ask.</summary>
    Task<bool?> IsPrivateReportingEnabledAsync(string repository, CancellationToken cancellationToken);
}

/// <summary>Reads organization membership.</summary>
internal interface IOrganizationReader
{
    /// <summary>Gets the permanent IDs of a team's members.</summary>
    Task<IReadOnlySet<long>> GetTeamMemberIdsAsync(string organization, string team, CancellationToken cancellationToken);

    /// <summary>Gets whether someone is an owner of an organization, or null when the token may not ask.</summary>
    Task<bool?> IsOrganizationOwnerAsync(string organization, string login, CancellationToken cancellationToken);
}

/// <summary>Reads pull requests and their reviews.</summary>
internal interface IPullRequestReader
{
    Task<PullRequest?> GetPullRequestAsync(string repository, int number, CancellationToken cancellationToken);

    Task<IReadOnlyList<PullRequest>> GetOpenPullRequestsAsync(string repository, CancellationToken cancellationToken);

    /// <summary>Finds the newest pull request from a branch of the repository, open or closed.</summary>
    Task<PullRequest?> FindPullRequestAsync(string repository, string branch, CancellationToken cancellationToken);

    Task<IReadOnlyList<PullRequestReview>> GetReviewsAsync(string repository, int number, CancellationToken cancellationToken);
}

/// <summary>Reads issues and comments.</summary>
internal interface IIssueReader
{
    Task<Issue?> GetIssueAsync(string repository, int number, CancellationToken cancellationToken);

    /// <summary>Lists issues (not pull requests) with a label.</summary>
    Task<IReadOnlyList<Issue>> GetIssuesAsync(string repository, string label, IssueFilter filter, CancellationToken cancellationToken);

    Task<IReadOnlyList<IssueComment>> GetCommentsAsync(string repository, int number, CancellationToken cancellationToken);
}

/// <summary>Everything the tool reads from GitHub.</summary>
internal interface IGitHubReader : IRepositoryReader, IOrganizationReader, IPullRequestReader, IIssueReader;
