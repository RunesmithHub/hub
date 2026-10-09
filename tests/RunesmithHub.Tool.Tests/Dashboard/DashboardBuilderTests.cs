using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Dashboard;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Json;
using RunesmithHub.Tool.Tests.Support;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Tests.Dashboard;

public sealed class DashboardBuilderTests : IDisposable
{
    private readonly TestRegistry _registry = new();
    private readonly TemporaryFolder _folder = new();
    private readonly FakeGitHub _github = new();

    public DashboardBuilderTests() => _registry.Publisher(Samples.Publisher()).Registration(Samples.Registration());

    public void Dispose()
    {
        _folder.Dispose();
        _registry.Dispose();
    }

    private async Task OpenVersionPullRequestAsync(int number, string version, VersionState state)
    {
        var record = await Samples.RecordAsync(await _registry.LoadAsync(), Samples.WriteBuildOutput(_folder.Combine(version), version), version) with
        {
            State = state,
            HoldUntil = state == VersionState.Held ? Samples.Time.AddHours(5) : null,
            TrackingIssue = 500 + number,
        };
        var head = new string((char)('a' + number % 6), 40);
        _github.PullRequests.Add(new PullRequest(number, $"Publish lumen.todo {version}", "", $"version/lumen.todo/{version}", head, FakeGitHub.Bot, [HubLabels.Version], true, false,
            Samples.Time.AddHours(-number), ""));
        _github.Files[$"RunesmithHub/registry:plugins/lumen.todo/versions/{version}.json"] = RegistryJson.Serialize(record);
    }

    [Fact]
    public async Task SummarisesTheQueueRejectionsActionsAuditFlagsAndReports()
    {
        await OpenVersionPullRequestAsync(1, "1.0.0", VersionState.Review);
        await OpenVersionPullRequestAsync(2, "1.1.0", VersionState.Held);
        _github.Issues.Add(new Issue(7, "lumen.todo 0.9.0", "", false, [HubLabels.Tracking, HubLabels.Rejected], FakeGitHub.Bot, Samples.Time.AddDays(-3), Samples.Time.AddDays(-2), ""));
        _github.Comments[7] = [new IssueComment(1, FakeGitHub.Bot, TrackingIssues.Rejection("Undeclared network use.", []), Samples.Time.AddDays(-2))];
        _github.Issues.Add(new Issue(8, "lumen.todo: Repository", "Moved to another owner.\n\nMore.", true, [HubLabels.Audit], FakeGitHub.Bot, Samples.Time, null, "https://github.com/RunesmithHub/registry/issues/8"));
        _github.Issues.Add(new Issue(9, "Broken", "### Plugin ID\n\nlumen.todo\n\n### What happens\n\nCrash.", true, [HubLabels.Broken], Samples.Maintainer, Samples.Time, null, ""));
        _github.Issues.Add(new Issue(10, "Policy", "### Plugin ID\n\nlumen.todo", true, [HubLabels.Policy], Samples.Maintainer, Samples.Time, null, ""));
        _registry.Moderation(new RegistryModerationEntry
        {
            Id = 1,
            Target = new ModerationTarget { Kind = ModerationTargetKind.Versions, Plugin = Samples.PluginId, Range = VersionRange.Parse("0.8.0") },
            Action = ModerationAction.Freeze,
            Reason = ModerationReason.CompromisedAccount,
            Note = "Paused.",
            CreatedAt = Samples.Time.AddDays(-5),
            CreatedBy = 9001,
            LiftedAt = Samples.Time.AddDays(-1),
            LiftedBy = 9001,
        });

        var summary = await new DashboardBuilder(_github, new FakeTimeProvider(Samples.Time)).BuildAsync(await _registry.LoadAsync(), TestContext.Current.CancellationToken);

        var review = Assert.Single(summary.ReviewQueue);
        Assert.Equal(("lumen.todo", "1.0.0", "unverified", 501), (review.Plugin, review.Version, review.Tier, review.TrackingIssue));
        Assert.Equal([ReviewTriggers.FirstVersion], review.Triggers);
        Assert.Equal(Samples.Time.AddHours(5), Assert.Single(summary.Held).HoldUntil);
        var rejected = Assert.Single(summary.Rejected);
        Assert.Equal(("0.9.0", "Undeclared network use.", 7), (rejected.Version, rejected.Reason, rejected.TrackingIssue));
        Assert.Equal(["unfreeze", "freeze"], summary.Actions.Select(action => action.Action));
        Assert.Equal("compromised account", summary.Actions[0].Reason);
        var flag = Assert.Single(summary.AuditFlags);
        Assert.Equal(("lumen.todo", "Repository", "Moved to another owner."), (flag.Plugin, flag.Kind, flag.Note));
        Assert.Equal(2, Assert.Single(summary.Reports).Open);
    }

    [Fact]
    public async Task WritesTheFieldsTheWebsiteReads()
    {
        await OpenVersionPullRequestAsync(2, "1.1.0", VersionState.Held);

        var summary = await new DashboardBuilder(_github, new FakeTimeProvider(Samples.Time)).BuildAsync(await _registry.LoadAsync(), TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(ToolJson.Serialize(summary));

        var held = json.RootElement.GetProperty("held")[0];
        Assert.Equal("2026-10-09T17:00:00Z", held.GetProperty("holdUntil").GetString());
        Assert.Equal(502, held.GetProperty("trackingIssue").GetInt32());
        Assert.False(held.TryGetProperty("triggers", out _));
        Assert.Equal("2026-10-09T12:00:00Z", json.RootElement.GetProperty("generatedAt").GetString());
    }
}
