using System.Globalization;
using System.Net;
using System.Text.Json;
using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Tool.GitHub;

/// <summary>Reads GitHub through its REST API.</summary>
internal sealed class GitHubReader(GitHubRestClient client) : IGitHubReader
{
    public async Task<GitHubRepository?> GetRepositoryAsync(long id, CancellationToken cancellationToken) =>
        await client.GetAsync(Invariant($"repositories/{id}"), cancellationToken).ConfigureAwait(false) is { } json ? Repository(json) : null;

    public async Task<GitHubUser?> GetUserAsync(long id, CancellationToken cancellationToken) =>
        await client.GetAsync(Invariant($"user/{id}"), cancellationToken).ConfigureAwait(false) is { } json ? User(json) : null;

    public async Task<GitHubUser?> GetUserAsync(string login, CancellationToken cancellationToken) =>
        await client.GetAsync($"users/{Escape(login)}", cancellationToken).ConfigureAwait(false) is { } json ? User(json) : null;

    public Task<byte[]?> GetFileAsync(string repository, string path, string? gitRef, CancellationToken cancellationToken)
    {
        var query = gitRef is null ? "" : "?ref=" + Uri.EscapeDataString(gitRef);
        var escaped = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        return client.GetBytesAsync($"repos/{repository}/contents/{escaped}{query}", "application/vnd.github.raw+json", cancellationToken);
    }

    public async Task<RepositoryPermission> GetPermissionAsync(string repository, string login, CancellationToken cancellationToken)
    {
        try
        {
            var json = await client.GetAsync($"repos/{repository}/collaborators/{Escape(login)}/permission", cancellationToken).ConfigureAwait(false);
            if (json is not { } value)
                return RepositoryPermission.None;
            var role = value.TryGetProperty("role_name", out var name) ? name.GetString() : value.GetProperty("permission").GetString();
            return role switch
            {
                "admin" => RepositoryPermission.Admin,
                "maintain" => RepositoryPermission.Maintain,
                "write" => RepositoryPermission.Write,
                "triage" => RepositoryPermission.Triage,
                "read" => RepositoryPermission.Read,
                _ => RepositoryPermission.None,
            };
        }
        catch (GitHubException exception) when (exception.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return RepositoryPermission.Unknown;
        }
    }

    public async Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(string repository, CancellationToken cancellationToken)
    {
        var items = await client.GetAllPagesAsync($"repos/{repository}/releases", cancellationToken).ConfigureAwait(false);
        return [.. items.Select(item => new GitHubRelease(
            item.GetProperty("id").GetInt64(),
            item.GetProperty("tag_name").GetString() ?? "",
            item.GetProperty("draft").GetBoolean(),
            item.GetProperty("prerelease").GetBoolean(),
            String(item, "body"),
            item.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object ? Account(author) : null))];
    }

    public async Task<string?> ResolveTagAsync(string repository, string tag, CancellationToken cancellationToken)
    {
        if (await client.GetAsync($"repos/{repository}/git/ref/tags/{Escape(tag)}", cancellationToken).ConfigureAwait(false) is not { } reference)
            return null;
        var target = reference.GetProperty("object");
        for (var depth = 0; depth < 5; depth++)
        {
            var type = target.GetProperty("type").GetString();
            var sha = target.GetProperty("sha").GetString();
            if (type == "commit")
                return sha;
            if (type != "tag" || await client.GetAsync($"repos/{repository}/git/tags/{sha}", cancellationToken).ConfigureAwait(false) is not { } annotated)
                return null;
            target = annotated.GetProperty("object");
        }

        return null;
    }

    public async Task<ReleaseWithAssets?> GetReleaseByTagAsync(string repository, string tag, CancellationToken cancellationToken) =>
        await client.GetAsync($"repos/{repository}/releases/tags/{Escape(tag)}", cancellationToken).ConfigureAwait(false) is { } json ? Release(json) : null;

    public async Task<BranchRules> GetBranchRulesAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        var rules = await client.GetAllPagesAsync($"repos/{repository}/rules/branches/{Escape(branch)}", cancellationToken).ConfigureAwait(false);
        var types = rules.Select(rule => rule.GetProperty("type").GetString()).ToHashSet(StringComparer.Ordinal);
        var legacy = await client.GetAsync($"repos/{repository}/branches/{Escape(branch)}", cancellationToken).ConfigureAwait(false);
        var isProtected = legacy is { } value && value.TryGetProperty("protected", out var flag) && flag.GetBoolean();
        return new BranchRules(types.Contains("deletion") || isProtected, types.Contains("non_fast_forward") || isProtected);
    }

    public async Task<bool?> IsPrivateReportingEnabledAsync(string repository, CancellationToken cancellationToken)
    {
        try
        {
            var json = await client.GetAsync($"repos/{repository}/private-vulnerability-reporting", cancellationToken).ConfigureAwait(false);
            return json is { } value && value.GetProperty("enabled").GetBoolean();
        }
        catch (GitHubException exception) when (exception.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return null;
        }
    }

    public async Task<IReadOnlySet<long>> GetTeamMemberIdsAsync(string organization, string team, CancellationToken cancellationToken)
    {
        var members = await client.GetAllPagesAsync($"orgs/{organization}/teams/{Escape(team)}/members", cancellationToken).ConfigureAwait(false);
        return members.Select(member => member.GetProperty("id").GetInt64()).ToHashSet();
    }

    public async Task<bool?> IsOrganizationOwnerAsync(string organization, string login, CancellationToken cancellationToken)
    {
        try
        {
            var json = await client.GetAsync($"orgs/{organization}/memberships/{Escape(login)}", cancellationToken).ConfigureAwait(false);
            return json is { } value && String(value, "state") == "active" && String(value, "role") == "admin";
        }
        catch (GitHubException exception) when (exception.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return null;
        }
    }

    public async Task<PullRequest?> GetPullRequestAsync(string repository, int number, CancellationToken cancellationToken) =>
        await client.GetAsync(Invariant($"repos/{repository}/pulls/{number}"), cancellationToken).ConfigureAwait(false) is { } json ? PullRequest(json) : null;

    public async Task<IReadOnlyList<PullRequest>> GetOpenPullRequestsAsync(string repository, CancellationToken cancellationToken) =>
        [.. (await client.GetAllPagesAsync($"repos/{repository}/pulls?state=open", cancellationToken).ConfigureAwait(false)).Select(PullRequest)];

    public async Task<PullRequest?> FindPullRequestAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        var owner = repository[..repository.IndexOf('/', StringComparison.Ordinal)];
        var pulls = await client.GetAllPagesAsync($"repos/{repository}/pulls?state=all&head={Uri.EscapeDataString($"{owner}:{branch}")}", cancellationToken).ConfigureAwait(false);
        return pulls.Select(PullRequest).OrderByDescending(pull => pull.Number).FirstOrDefault();
    }

    public async Task<IReadOnlyList<PullRequestReview>> GetReviewsAsync(string repository, int number, CancellationToken cancellationToken) =>
        [.. (await client.GetAllPagesAsync(Invariant($"repos/{repository}/pulls/{number}/reviews"), cancellationToken).ConfigureAwait(false))
            .Where(review => review.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
            .Select(review => new PullRequestReview(
                Account(review.GetProperty("user")),
                String(review, "state"),
                String(review, "commit_id"),
                Time(review, "submitted_at"),
                String(review, "body")))];

    public async Task<Issue?> GetIssueAsync(string repository, int number, CancellationToken cancellationToken) =>
        await client.GetAsync(Invariant($"repos/{repository}/issues/{number}"), cancellationToken).ConfigureAwait(false) is { } json ? Issue(json) : null;

    public async Task<IReadOnlyList<Issue>> GetIssuesAsync(string repository, string label, IssueFilter filter, CancellationToken cancellationToken)
    {
        var state = filter.ToString().ToLowerInvariant();
        var items = await client.GetAllPagesAsync($"repos/{repository}/issues?state={state}&labels={Uri.EscapeDataString(label)}", cancellationToken).ConfigureAwait(false);
        return [.. items.Where(item => !item.TryGetProperty("pull_request", out _)).Select(Issue)];
    }

    public async Task<IReadOnlyList<IssueComment>> GetCommentsAsync(string repository, int number, CancellationToken cancellationToken) =>
        [.. (await client.GetAllPagesAsync(Invariant($"repos/{repository}/issues/{number}/comments"), cancellationToken).ConfigureAwait(false))
            .Select(comment => new IssueComment(
                comment.GetProperty("id").GetInt64(),
                Account(comment.GetProperty("user")),
                String(comment, "body"),
                Time(comment, "created_at") ?? DateTimeOffset.MinValue))];

    internal static ReleaseWithAssets Release(JsonElement json) => new(
        json.GetProperty("id").GetInt64(),
        String(json, "upload_url"),
        String(json, "html_url"),
        [.. json.GetProperty("assets").EnumerateArray().Select(asset => new ReleaseAsset(
            asset.GetProperty("id").GetInt64(),
            String(asset, "name"),
            asset.GetProperty("size").GetInt64(),
            String(asset, "digest") is { Length: > 7 } digest && digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest[7..] : null))]);

    private static GitHubRepository Repository(JsonElement json)
    {
        var owner = json.GetProperty("owner");
        return new GitHubRepository(
            json.GetProperty("id").GetInt64(),
            String(json, "full_name"),
            String(json, "html_url"),
            new GitHubOwner(owner.GetProperty("id").GetInt64(), String(owner, "login"), OwnerKind(owner)),
            json.GetProperty("private").GetBoolean(),
            json.GetProperty("fork").GetBoolean(),
            json.TryGetProperty("archived", out var archived) && archived.GetBoolean(),
            String(json, "default_branch"));
    }

    private static GitHubUser User(JsonElement json) =>
        new(json.GetProperty("id").GetInt64(), String(json, "login"), OwnerKind(json), Time(json, "created_at") ?? DateTimeOffset.MinValue);

    private static PullRequest PullRequest(JsonElement json) => new(
        json.GetProperty("number").GetInt32(),
        String(json, "title"),
        String(json, "body"),
        String(json.GetProperty("head"), "ref"),
        String(json.GetProperty("head"), "sha"),
        Account(json.GetProperty("user")),
        Labels(json),
        String(json, "state") == "open",
        json.TryGetProperty("merged_at", out var merged) && merged.ValueKind == JsonValueKind.String,
        Time(json, "created_at") ?? DateTimeOffset.MinValue,
        String(json, "html_url"));

    private static Issue Issue(JsonElement json) => new(
        json.GetProperty("number").GetInt32(),
        String(json, "title"),
        String(json, "body"),
        String(json, "state") == "open",
        Labels(json),
        Account(json.GetProperty("user")),
        Time(json, "created_at") ?? DateTimeOffset.MinValue,
        Time(json, "closed_at"),
        String(json, "html_url"));

    private static IReadOnlyList<string> Labels(JsonElement json) =>
        json.TryGetProperty("labels", out var labels) ? [.. labels.EnumerateArray().Select(label => String(label, "name"))] : [];

    private static GitHubAccount Account(JsonElement json) => new() { Id = json.GetProperty("id").GetInt64(), Login = String(json, "login") };

    private static OwnerType OwnerKind(JsonElement json) => String(json, "type") == "Organization" ? OwnerType.Organization : OwnerType.User;

    private static string String(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static DateTimeOffset? Time(JsonElement json, string name) =>
        DateTimeOffset.TryParse(String(json, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
