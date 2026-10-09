using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Tool.GitHub;

/// <summary>A GitHub repository as the hub checks it.</summary>
internal sealed record GitHubRepository(long Id, string FullName, string HtmlUrl, GitHubOwner Owner, bool Private, bool Fork, bool Archived, string DefaultBranch);

/// <summary>The user or organization that owns a repository.</summary>
internal sealed record GitHubOwner(long Id, string Login, OwnerType Type);

/// <summary>A GitHub user or organization account.</summary>
internal sealed record GitHubUser(long Id, string Login, OwnerType Type, DateTimeOffset CreatedAt);

/// <summary>A release of a plugin repository.</summary>
internal sealed record GitHubRelease(long Id, string TagName, bool Draft, bool Prerelease, string Body, GitHubAccount? Author);

/// <summary>A release with its uploaded assets.</summary>
internal sealed record ReleaseWithAssets(long Id, string UploadUrl, string HtmlUrl, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>An uploaded release asset; the SHA-256 is null when GitHub did not report a digest.</summary>
internal sealed record ReleaseAsset(long Id, string Name, long Size, string? Sha256);

/// <summary>A pull request.</summary>
internal sealed record PullRequest(
    int Number, string Title, string Body, string HeadRef, string HeadSha, GitHubAccount Author, IReadOnlyList<string> Labels, bool Open, bool Merged,
    DateTimeOffset CreatedAt, string HtmlUrl);

/// <summary>A review on a pull request; <paramref name="State"/> is GitHub's, such as <c>APPROVED</c>.</summary>
internal sealed record PullRequestReview(GitHubAccount Author, string State, string CommitId, DateTimeOffset? SubmittedAt, string Body = "");

/// <summary>An issue, or a pull request seen through the issues API.</summary>
internal sealed record Issue(
    int Number, string Title, string Body, bool Open, IReadOnlyList<string> Labels, GitHubAccount Author, DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt,
    string HtmlUrl);

/// <summary>A comment on an issue or pull request.</summary>
internal sealed record IssueComment(long Id, GitHubAccount Author, string Body, DateTimeOffset CreatedAt);

/// <summary>Someone's permission on a repository; <see cref="Unknown"/> when the token may not ask.</summary>
internal enum RepositoryPermission
{
    Unknown,
    None,
    Read,
    Triage,
    Write,
    Maintain,
    Admin,
}

/// <summary>Which issues to list.</summary>
internal enum IssueFilter
{
    Open,
    Closed,
    All,
}

/// <summary>The outcome of a commit status.</summary>
internal enum CommitStatusState
{
    Pending,
    Success,
    Failure,
}

/// <summary>The rules that protect a branch, as anyone can read them for a public repository.</summary>
internal sealed record BranchRules(bool BlocksDeletion, bool BlocksForcePushes);
