using System.Globalization;
using System.Text.RegularExpressions;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Tool.GitHub;

namespace RunesmithHub.Tool.Gates;

/// <summary>An approval of a pull request's current commit, by GitHub user ID.</summary>
internal sealed record Approval(GitHubAccount By, DateTimeOffset At);

/// <summary>
/// Reads who approved a pull request's current commit: approving reviews, and approvals the hub bot recorded for an administrator's
/// <c>/approve</c> command as a marker comment.
/// </summary>
internal static partial class Approvals
{
    /// <summary>Writes the marker the bot comments for an <c>/approve</c> command.</summary>
    public static string Marker(GitHubAccount by, string headSha) =>
        string.Create(CultureInfo.InvariantCulture, $"<!-- hub-approve: {by.Id} {by.Login} {headSha} -->\nApproved by @{by.Login} with /approve.\n");

    public static IReadOnlyList<Approval> Of(IEnumerable<PullRequestReview> reviews, IEnumerable<IssueComment> comments, string headSha, string? botLogin)
    {
        var latest = reviews
            .Where(review => review.State is "APPROVED" or "CHANGES_REQUESTED" or "DISMISSED")
            .GroupBy(review => review.Author.Id)
            .Select(group => group.OrderBy(review => review.SubmittedAt).Last())
            .Where(review => review.State == "APPROVED" && string.Equals(review.CommitId, headSha, StringComparison.Ordinal))
            .Select(review => new Approval(review.Author, review.SubmittedAt ?? DateTimeOffset.MinValue));

        var recorded = comments
            .Where(comment => botLogin is not null && string.Equals(comment.Author.Login, botLogin, StringComparison.Ordinal))
            .Select(comment => (Comment: comment, Match: MarkerPattern().Match(comment.Body)))
            .Where(pair => pair.Match.Success && string.Equals(pair.Match.Groups[3].Value, headSha, StringComparison.Ordinal))
            .Select(pair => new Approval(
                new GitHubAccount { Id = long.Parse(pair.Match.Groups[1].Value, CultureInfo.InvariantCulture), Login = pair.Match.Groups[2].Value },
                pair.Comment.CreatedAt));

        return [.. latest.Concat(recorded).GroupBy(approval => approval.By.Id).Select(group => group.MinBy(approval => approval.At)!)];
    }

    /// <summary>Gets whether a review requested changes on the current commit, which rejects a version.</summary>
    public static bool ChangesRequested(IEnumerable<PullRequestReview> reviews, IReadOnlySet<long> admins) =>
        reviews.GroupBy(review => review.Author.Id)
            .Select(group => group.OrderBy(review => review.SubmittedAt).Last())
            .Any(review => review.State == "CHANGES_REQUESTED" && admins.Contains(review.Author.Id));

    [GeneratedRegex("\\A<!-- hub-approve: ([0-9]+) ([A-Za-z0-9-]+(?:\\[bot\\])?) ([0-9a-f]{40}) -->(?:\\r?\\n|\\z)")]
    private static partial Regex MarkerPattern();
}
