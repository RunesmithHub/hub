using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RunesmithHub.Tool.GitHub;

/// <summary>Acts on GitHub as the hub bot, through its REST and GraphQL APIs.</summary>
internal sealed class GitHubWriter(GitHubRestClient client) : IGitHubWriter
{
    private const string AutoMergeMutation =
        "mutation($id: ID!) { enablePullRequestAutoMerge(input: { pullRequestId: $id, mergeMethod: SQUASH }) { clientMutationId } }";

    public async Task<int> CreateIssueAsync(string repository, string title, string body, IReadOnlyList<string> labels, CancellationToken cancellationToken)
    {
        var issue = await client.SendJsonAsync(HttpMethod.Post, $"repos/{repository}/issues",
            new JsonObject { ["title"] = title, ["body"] = body, ["labels"] = Array(labels) }, cancellationToken).ConfigureAwait(false);
        return issue.GetProperty("number").GetInt32();
    }

    public Task CommentAsync(string repository, int number, string body, CancellationToken cancellationToken) =>
        client.SendJsonAsync(HttpMethod.Post, Invariant($"repos/{repository}/issues/{number}/comments"), new JsonObject { ["body"] = body }, cancellationToken);

    public Task AddLabelsAsync(string repository, int number, IReadOnlyList<string> labels, CancellationToken cancellationToken) =>
        client.SendJsonAsync(HttpMethod.Post, Invariant($"repos/{repository}/issues/{number}/labels"), new JsonObject { ["labels"] = Array(labels) }, cancellationToken);

    public Task CloseIssueAsync(string repository, int number, CancellationToken cancellationToken) =>
        client.SendJsonAsync(HttpMethod.Patch, Invariant($"repos/{repository}/issues/{number}"), new JsonObject { ["state"] = "closed" }, cancellationToken);

    public async Task<int> OpenPullRequestAsync(string repository, ChangeSet changes, CancellationToken cancellationToken)
    {
        var repo = await client.SendJsonAsync(HttpMethod.Get, $"repos/{repository}", null, cancellationToken).ConfigureAwait(false);
        var defaultBranch = repo.GetProperty("default_branch").GetString()!;
        var baseSha = await HeadAsync(repository, defaultBranch, cancellationToken).ConfigureAwait(false);
        var commit = await CommitAsync(repository, baseSha, changes.Title, changes.Files, cancellationToken).ConfigureAwait(false);
        await client.SendJsonAsync(HttpMethod.Post, $"repos/{repository}/git/refs",
            new JsonObject { ["ref"] = "refs/heads/" + changes.Branch, ["sha"] = commit }, cancellationToken).ConfigureAwait(false);

        var pull = await client.SendJsonAsync(HttpMethod.Post, $"repos/{repository}/pulls",
            new JsonObject { ["title"] = changes.Title, ["head"] = changes.Branch, ["base"] = defaultBranch, ["body"] = changes.Body }, cancellationToken).ConfigureAwait(false);
        var number = pull.GetProperty("number").GetInt32();
        if (changes.Labels.Count > 0)
            await AddLabelsAsync(repository, number, changes.Labels, cancellationToken).ConfigureAwait(false);
        if (changes.AutoMerge)
            await client.GraphQlAsync(AutoMergeMutation, new JsonObject { ["id"] = pull.GetProperty("node_id").GetString() }, cancellationToken).ConfigureAwait(false);
        return number;
    }

    public async Task UpdateBranchAsync(string repository, string branch, string message, IReadOnlyDictionary<string, byte[]?> files, CancellationToken cancellationToken)
    {
        var head = await HeadAsync(repository, branch, cancellationToken).ConfigureAwait(false);
        var commit = await CommitAsync(repository, head, message, files, cancellationToken).ConfigureAwait(false);
        await client.SendJsonAsync(HttpMethod.Patch, $"repos/{repository}/git/refs/heads/{branch}", new JsonObject { ["sha"] = commit }, cancellationToken).ConfigureAwait(false);
    }

    public Task ClosePullRequestAsync(string repository, int number, CancellationToken cancellationToken) =>
        client.SendJsonAsync(HttpMethod.Patch, Invariant($"repos/{repository}/pulls/{number}"), new JsonObject { ["state"] = "closed" }, cancellationToken);

    public Task SetStatusAsync(string repository, string sha, string context, CommitStatusState state, string description, CancellationToken cancellationToken) =>
        client.SendJsonAsync(HttpMethod.Post, $"repos/{repository}/statuses/{sha}", new JsonObject
        {
            ["state"] = state.ToString().ToLowerInvariant(),
            ["context"] = context,
            ["description"] = description.Length > 140 ? description[..137] + "..." : description,
        }, cancellationToken);

    public async Task<ReleaseWithAssets> EnsureReleaseAsync(string repository, string tag, string name, string body, CancellationToken cancellationToken)
    {
        try
        {
            var release = await client.SendJsonAsync(HttpMethod.Post, $"repos/{repository}/releases",
                new JsonObject { ["tag_name"] = tag, ["name"] = name, ["body"] = body }, cancellationToken).ConfigureAwait(false);
            return GitHubReader.Release(release);
        }
        catch (GitHubException exception) when (exception.Status == HttpStatusCode.UnprocessableEntity)
        {
            var existing = await client.SendJsonAsync(HttpMethod.Get, $"repos/{repository}/releases/tags/{Uri.EscapeDataString(tag)}", null, cancellationToken).ConfigureAwait(false);
            return GitHubReader.Release(existing);
        }
    }

    public async Task UploadAssetAsync(ReleaseWithAssets release, string name, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var template = release.UploadUrl;
        var brace = template.IndexOf('{', StringComparison.Ordinal);
        var url = new Uri((brace < 0 ? template : template[..brace]) + "?name=" + Uri.EscapeDataString(name));
        await client.UploadAsync(url, content, contentType, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAssetAsync(string repository, long assetId, CancellationToken cancellationToken) =>
        client.SendJsonAsync(HttpMethod.Delete, Invariant($"repos/{repository}/releases/assets/{assetId}"), null, cancellationToken);

    public async Task<string> DraftAdvisoryAsync(string repository, string summary, string description, CancellationToken cancellationToken)
    {
        var advisory = await client.SendJsonAsync(HttpMethod.Post, $"repos/{repository}/security-advisories", new JsonObject
        {
            ["summary"] = summary,
            ["description"] = description,
            ["vulnerabilities"] = new JsonArray(),
        }, cancellationToken).ConfigureAwait(false);
        return advisory.GetProperty("html_url").GetString() ?? "";
    }

    private async Task<string> HeadAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        var reference = await client.SendJsonAsync(HttpMethod.Get, $"repos/{repository}/git/ref/heads/{branch}", null, cancellationToken).ConfigureAwait(false);
        return reference.GetProperty("object").GetProperty("sha").GetString()!;
    }

    /// <summary>Creates a commit on a parent with the files changed, without a custom author, so GitHub signs it as the bot.</summary>
    private async Task<string> CommitAsync(string repository, string parent, string message, IReadOnlyDictionary<string, byte[]?> files, CancellationToken cancellationToken)
    {
        var parentCommit = await client.SendJsonAsync(HttpMethod.Get, $"repos/{repository}/git/commits/{parent}", null, cancellationToken).ConfigureAwait(false);
        var entries = new JsonArray();
        foreach (var (path, content) in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var entry = new JsonObject { ["path"] = path, ["mode"] = "100644", ["type"] = "blob" };
            if (content is null)
                entry["sha"] = null;
            else
                entry["content"] = Encoding.UTF8.GetString(content);
            entries.Add(entry);
        }

        var tree = await client.SendJsonAsync(HttpMethod.Post, $"repos/{repository}/git/trees",
            new JsonObject { ["base_tree"] = parentCommit.GetProperty("tree").GetProperty("sha").GetString(), ["tree"] = entries }, cancellationToken).ConfigureAwait(false);
        var commit = await client.SendJsonAsync(HttpMethod.Post, $"repos/{repository}/git/commits", new JsonObject
        {
            ["message"] = message,
            ["tree"] = tree.GetProperty("sha").GetString(),
            ["parents"] = new JsonArray(parent),
        }, cancellationToken).ConfigureAwait(false);
        return commit.GetProperty("sha").GetString()!;
    }

    private static JsonArray Array(IEnumerable<string> values) => new([.. values.Select(value => (JsonNode?)JsonValue.Create(value))]);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
