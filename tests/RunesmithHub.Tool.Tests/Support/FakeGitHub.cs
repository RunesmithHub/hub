using System.Globalization;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Tool.GitHub;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>An in-memory GitHub for tests: what the tool reads is set up in the dictionaries, and what the bot does is recorded.</summary>
internal sealed class FakeGitHub : IGitHubReader, IGitHubWriter
{
    private int _nextNumber = 100;

    public Dictionary<long, GitHubRepository> Repositories { get; } = [];

    public Dictionary<long, GitHubUser> Users { get; } = [];

    /// <summary>Gets files by <c>repository:path</c>, at any ref.</summary>
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets permissions by <c>repository:login</c>; anything missing is <see cref="RepositoryPermission.None"/>.</summary>
    public Dictionary<string, RepositoryPermission> Permissions { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, bool?> OrganizationOwners { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<GitHubRelease>> Releases { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets tags by <c>repository:tag</c>, pointing at commits.</summary>
    public Dictionary<string, string> Tags { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ReleaseWithAssets> PackageReleases { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, HashSet<long>> Teams { get; } = new(StringComparer.Ordinal) { ["admins"] = [], ["owners"] = [] };

    public List<PullRequest> PullRequests { get; } = [];

    public Dictionary<int, List<PullRequestReview>> Reviews { get; } = [];

    public List<Issue> Issues { get; } = [];

    public Dictionary<int, List<IssueComment>> Comments { get; } = [];

    public BranchRules BranchRules { get; set; } = new(true, true);

    public bool? PrivateReporting { get; set; } = true;

    public List<(int Number, ChangeSet Changes)> OpenedPullRequests { get; } = [];

    public List<(string Branch, string Message, IReadOnlyDictionary<string, byte[]?> Files)> BranchUpdates { get; } = [];

    public List<(string Sha, string Context, CommitStatusState State, string Description)> Statuses { get; } = [];

    public List<int> ClosedPullRequests { get; } = [];

    public List<(string Release, string Name, byte[] Content)> Uploads { get; } = [];

    public List<string> Advisories { get; } = [];

    public List<string> DeletedAssets { get; } = [];

    public GitHubUser AddUser(long id, string login, OwnerType type = OwnerType.User, DateTimeOffset? createdAt = null)
    {
        var user = new GitHubUser(id, login, type, createdAt ?? new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Users[id] = user;
        return user;
    }

    public GitHubRepository AddRepository(long id, string fullName, GitHubUser owner, bool isPrivate = false, bool fork = false)
    {
        var repository = new GitHubRepository(id, fullName, $"https://github.com/{fullName}", new GitHubOwner(owner.Id, owner.Login, owner.Type), isPrivate, fork, false, "main");
        Repositories[id] = repository;
        return repository;
    }

    public IReadOnlyList<IssueComment> CommentsOn(int number) => Comments.GetValueOrDefault(number) ?? [];

    public Issue IssueNumber(int number) => Issues.Single(issue => issue.Number == number);

    public Task<GitHubRepository?> GetRepositoryAsync(long id, CancellationToken cancellationToken) => Task.FromResult(Repositories.GetValueOrDefault(id));

    public Task<GitHubUser?> GetUserAsync(long id, CancellationToken cancellationToken) => Task.FromResult(Users.GetValueOrDefault(id));

    public Task<GitHubUser?> GetUserAsync(string login, CancellationToken cancellationToken) =>
        Task.FromResult(Users.Values.FirstOrDefault(user => string.Equals(user.Login, login, StringComparison.OrdinalIgnoreCase)));

    public Task<byte[]?> GetFileAsync(string repository, string path, string? gitRef, CancellationToken cancellationToken) =>
        Task.FromResult(Files.GetValueOrDefault($"{repository}:{path}"));

    public Task<RepositoryPermission> GetPermissionAsync(string repository, string login, CancellationToken cancellationToken) =>
        Task.FromResult(Permissions.GetValueOrDefault($"{repository}:{login}", RepositoryPermission.None));

    public Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(string repository, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitHubRelease>>(Releases.GetValueOrDefault(repository) ?? []);

    public Task<string?> ResolveTagAsync(string repository, string tag, CancellationToken cancellationToken) => Task.FromResult(Tags.GetValueOrDefault($"{repository}:{tag}"));

    public Task<ReleaseWithAssets?> GetReleaseByTagAsync(string repository, string tag, CancellationToken cancellationToken) => Task.FromResult(PackageReleases.GetValueOrDefault(tag));

    public Task<BranchRules> GetBranchRulesAsync(string repository, string branch, CancellationToken cancellationToken) => Task.FromResult(BranchRules);

    public Task<bool?> IsPrivateReportingEnabledAsync(string repository, CancellationToken cancellationToken) => Task.FromResult(PrivateReporting);

    public Task<IReadOnlySet<long>> GetTeamMemberIdsAsync(string organization, string team, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<long>>(Teams.GetValueOrDefault(team) ?? []);

    public Task<bool?> IsOrganizationOwnerAsync(string organization, string login, CancellationToken cancellationToken) =>
        Task.FromResult(OrganizationOwners.GetValueOrDefault($"{organization}:{login}", false));

    public Task<PullRequest?> GetPullRequestAsync(string repository, int number, CancellationToken cancellationToken) =>
        Task.FromResult(PullRequests.FirstOrDefault(pull => pull.Number == number));

    public Task<IReadOnlyList<PullRequest>> GetOpenPullRequestsAsync(string repository, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PullRequest>>([.. PullRequests.Where(pull => pull.Open)]);

    public Task<PullRequest?> FindPullRequestAsync(string repository, string branch, CancellationToken cancellationToken) =>
        Task.FromResult(PullRequests.Where(pull => pull.HeadRef == branch).MaxBy(pull => pull.Number));

    public Task<IReadOnlyList<PullRequestReview>> GetReviewsAsync(string repository, int number, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PullRequestReview>>(Reviews.GetValueOrDefault(number) ?? []);

    public Task<Issue?> GetIssueAsync(string repository, int number, CancellationToken cancellationToken) =>
        Task.FromResult(Issues.FirstOrDefault(issue => issue.Number == number));

    public Task<IReadOnlyList<Issue>> GetIssuesAsync(string repository, string label, IssueFilter filter, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Issue>>([.. Issues.Where(issue => issue.Labels.Contains(label) && filter switch
        {
            IssueFilter.Open => issue.Open,
            IssueFilter.Closed => !issue.Open,
            _ => true,
        })]);

    public Task<IReadOnlyList<IssueComment>> GetCommentsAsync(string repository, int number, CancellationToken cancellationToken) =>
        Task.FromResult(CommentsOn(number));

    public Task<int> CreateIssueAsync(string repository, string title, string body, IReadOnlyList<string> labels, CancellationToken cancellationToken)
    {
        var number = ++_nextNumber;
        Issues.Add(new Issue(number, title, body, true, labels, Bot, Now, null, $"https://github.com/{repository}/issues/{number}"));
        return Task.FromResult(number);
    }

    public Task CommentAsync(string repository, int number, string body, CancellationToken cancellationToken)
    {
        if (!Comments.TryGetValue(number, out var list))
            Comments[number] = list = [];
        list.Add(new IssueComment(list.Count + 1, Bot, body, Now));
        return Task.CompletedTask;
    }

    public Task AddLabelsAsync(string repository, int number, IReadOnlyList<string> labels, CancellationToken cancellationToken)
    {
        var index = Issues.FindIndex(issue => issue.Number == number);
        if (index >= 0)
            Issues[index] = Issues[index] with { Labels = [.. Issues[index].Labels.Union(labels)] };
        return Task.CompletedTask;
    }

    public Task CloseIssueAsync(string repository, int number, CancellationToken cancellationToken)
    {
        var index = Issues.FindIndex(issue => issue.Number == number);
        if (index >= 0)
            Issues[index] = Issues[index] with { Open = false, ClosedAt = Now };
        return Task.CompletedTask;
    }

    public Task<int> OpenPullRequestAsync(string repository, ChangeSet changes, CancellationToken cancellationToken)
    {
        var number = ++_nextNumber;
        OpenedPullRequests.Add((number, changes));
        return Task.FromResult(number);
    }

    public Task UpdateBranchAsync(string repository, string branch, string message, IReadOnlyDictionary<string, byte[]?> files, CancellationToken cancellationToken)
    {
        BranchUpdates.Add((branch, message, files));
        return Task.CompletedTask;
    }

    public Task ClosePullRequestAsync(string repository, int number, CancellationToken cancellationToken)
    {
        ClosedPullRequests.Add(number);
        return Task.CompletedTask;
    }

    public Task SetStatusAsync(string repository, string sha, string context, CommitStatusState state, string description, CancellationToken cancellationToken)
    {
        Statuses.Add((sha, context, state, description));
        return Task.CompletedTask;
    }

    public Task<ReleaseWithAssets> EnsureReleaseAsync(string repository, string tag, string name, string body, CancellationToken cancellationToken)
    {
        if (!PackageReleases.TryGetValue(tag, out var release))
            PackageReleases[tag] = release = new ReleaseWithAssets(PackageReleases.Count + 1, $"https://uploads.example/{tag}", $"https://github.com/{repository}/releases/tag/{tag}", []);
        return Task.FromResult(release);
    }

    public Task UploadAssetAsync(ReleaseWithAssets release, string name, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        Uploads.Add((release.HtmlUrl, name, content));
        var tag = PackageReleases.Single(pair => pair.Value.Id == release.Id).Key;
        PackageReleases[tag] = PackageReleases[tag] with { Assets = [.. PackageReleases[tag].Assets, new ReleaseAsset(Uploads.Count, name, content.Length, Protocol.Hashes.Sha256(content))] };
        return Task.CompletedTask;
    }

    public Task DeleteAssetAsync(string repository, long assetId, CancellationToken cancellationToken)
    {
        var (tag, release) = PackageReleases.Single(pair => pair.Value.Assets.Any(asset => asset.Id == assetId));
        DeletedAssets.Add(release.Assets.Single(asset => asset.Id == assetId).Name);
        PackageReleases[tag] = release with { Assets = [.. release.Assets.Where(asset => asset.Id != assetId)] };
        return Task.CompletedTask;
    }

    /// <summary>Gets the content of a release asset that is still there, or null.</summary>
    public byte[]? DownloadAsset(string tag, string name) =>
        PackageReleases.GetValueOrDefault(tag)?.Assets.FirstOrDefault(asset => asset.Name == name) is { } asset ? Uploads[(int)asset.Id - 1].Content : null;

    public Task<string> DraftAdvisoryAsync(string repository, string summary, string description, CancellationToken cancellationToken)
    {
        Advisories.Add(summary);
        return Task.FromResult(string.Create(CultureInfo.InvariantCulture, $"https://github.com/{repository}/security/advisories/GHSA-{Advisories.Count}"));
    }

    public static GitHubAccount Bot { get; } = new() { Id = 999, Login = "runesmith-hub-bot[bot]" };

    private static DateTimeOffset Now { get; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
}
