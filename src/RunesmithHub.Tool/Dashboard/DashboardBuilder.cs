using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Audit;
using RunesmithHub.Tool.Developer;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Moderation;
using RunesmithHub.Tool.Registry;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Dashboard;

/// <summary>
/// Builds the public moderation dashboard from GitHub and the registry: open version pull requests (in review or held), versions rejected
/// in the last 30 days, the moderation log, open audit issues, and open public reports per plugin. Private reports are never read.
/// </summary>
internal sealed class DashboardBuilder(IGitHubReader github, TimeProvider time)
{
    private const int MaxActions = 50;
    private static readonly TimeSpan RejectedWindow = TimeSpan.FromDays(30);
    private static readonly string[] ReportLabels = [HubLabels.Broken, HubLabels.Policy];

    public async Task<DashboardSummary> BuildAsync(RegistryState registry, CancellationToken cancellationToken)
    {
        var now = Protocol.Json.UtcTimestampConverter.Truncate(time.GetUtcNow());
        var review = new List<DashboardVersion>();
        var held = new List<DashboardVersion>();
        foreach (var pull in await github.GetOpenPullRequestsAsync(HubLocations.Registry, cancellationToken).ConfigureAwait(false))
        {
            if (!pull.Labels.Contains(HubLabels.Version) || !pull.HeadRef.StartsWith("version/", StringComparison.Ordinal))
                continue;
            var parts = pull.HeadRef.Split('/');
            if (parts.Length != 3 || !TrackingIssues.TryParseTitle($"{parts[1]} {parts[2]}", out var pluginId, out var version))
                continue;
            var path = RegistryPaths.Version(pluginId, version);
            if (await github.GetFileAsync(HubLocations.Registry, path, pull.HeadSha, cancellationToken).ConfigureAwait(false) is not { } bytes)
                continue;
            var record = RegistryReader.Parse<RegistryVersionRecord>(bytes, path);
            var tier = (registry.FindRegistration(pluginId)?.Tier ?? PluginTier.Unverified).ToString().ToLowerInvariant();
            var item = new DashboardVersion(pluginId, version.ToString(), tier, pull.CreatedAt, record.HoldUntil, record.TrackingIssue);
            if (record.State == VersionState.Review)
                review.Add(item with { Triggers = record.Analysis.Triggers });
            else if (record.State == VersionState.Held)
                held.Add(item);
        }

        return new DashboardSummary
        {
            GeneratedAt = now,
            ReviewQueue = [.. review.OrderBy(item => item.Since)],
            Held = [.. held.OrderBy(item => item.HoldUntil)],
            Rejected = await RejectedAsync(now, cancellationToken).ConfigureAwait(false),
            Actions = Actions(registry),
            AuditFlags = [.. (await github.GetIssuesAsync(HubLocations.Registry, HubLabels.Audit, IssueFilter.Open, cancellationToken).ConfigureAwait(false))
                .Select(issue => (Issue: issue, Title: AuditFinding.ParseTitle(issue.Title)))
                .Where(pair => pair.Title is not null)
                .Select(pair => new DashboardAuditFlag(pair.Title!.Value.Subject, pair.Title.Value.Kind, FirstLine(pair.Issue.Body)) { Url = pair.Issue.HtmlUrl })],
            Reports = await ReportsAsync(cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<IReadOnlyList<DashboardRejection>> RejectedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rejected = new List<DashboardRejection>();
        var issues = await github.GetIssuesAsync(HubLocations.Registry, HubLabels.Rejected, IssueFilter.Closed, cancellationToken).ConfigureAwait(false);
        foreach (var issue in issues.Where(issue => issue.ClosedAt >= now - RejectedWindow).OrderByDescending(issue => issue.ClosedAt))
        {
            if (!TrackingIssues.TryParseTitle(issue.Title, out var pluginId, out var version))
                continue;
            var comments = await github.GetCommentsAsync(HubLocations.Registry, issue.Number, cancellationToken).ConfigureAwait(false);
            var reason = comments.Select(comment => TrackingIssues.RejectionReason(comment.Body)).LastOrDefault(text => text is not null) ?? "Rejected";
            rejected.Add(new DashboardRejection(pluginId, version.ToString(), reason) { At = issue.ClosedAt, TrackingIssue = issue.Number });
        }

        return rejected;
    }

    private static IReadOnlyList<DashboardAction> Actions(RegistryState registry) =>
        [.. registry.Moderation
            .SelectMany(entry => new[]
            {
                new DashboardAction(entry.Id, entry.Action.ToString().ToLowerInvariant(), entry.Target.ToString(), Reasons.Text(entry.Reason), entry.CreatedAt),
            }.Concat(entry.LiftedAt is { } lifted
                ? [new DashboardAction(entry.Id, "un" + entry.Action.ToString().ToLowerInvariant(), entry.Target.ToString(), Reasons.Text(entry.Reason), lifted)]
                : []))
            .OrderByDescending(action => action.At)
            .Take(MaxActions)];

    private async Task<IReadOnlyList<DashboardReports>> ReportsAsync(CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var label in ReportLabels)
        {
            foreach (var issue in await github.GetIssuesAsync(HubLocations.Registry, label, IssueFilter.Open, cancellationToken).ConfigureAwait(false))
            {
                var plugin = IssueForm.ParseBody(issue.Body).GetValueOrDefault("Plugin ID");
                if (plugin is { Length: > 0 } && Protocol.PluginId.IsValid(plugin))
                    counts[plugin] = counts.GetValueOrDefault(plugin) + 1;
            }
        }

        return [.. counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new DashboardReports(pair.Key, pair.Value))];
    }

    private static string FirstLine(string text) =>
        text.ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";
}
