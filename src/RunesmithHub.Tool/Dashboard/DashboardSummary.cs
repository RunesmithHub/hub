namespace RunesmithHub.Tool.Dashboard;

/// <summary>The moderation dashboard summary, <c>dashboard.json</c>: unsigned and informational.</summary>
internal sealed record DashboardSummary
{
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Gets the versions waiting for review, oldest first.</summary>
    public required IReadOnlyList<DashboardVersion> ReviewQueue { get; init; }

    /// <summary>Gets the versions in their hold, with when it ends.</summary>
    public required IReadOnlyList<DashboardVersion> Held { get; init; }

    public required IReadOnlyList<DashboardRejection> Rejected { get; init; }

    /// <summary>Gets the moderation log, newest first.</summary>
    public required IReadOnlyList<DashboardAction> Actions { get; init; }

    public required IReadOnlyList<DashboardAuditFlag> AuditFlags { get; init; }

    /// <summary>Gets the open public reports per plugin; private reports are never listed.</summary>
    public required IReadOnlyList<DashboardReports> Reports { get; init; }
}

internal sealed record DashboardVersion(string Plugin, string Version, string Tier, DateTimeOffset Since, DateTimeOffset? HoldUntil, int TrackingIssue)
{
    /// <summary>Gets the review trigger codes, for versions in review.</summary>
    public IReadOnlyList<string>? Triggers { get; init; }
}

internal sealed record DashboardRejection(string Plugin, string Version, string Reason)
{
    public DateTimeOffset? At { get; init; }

    public int? TrackingIssue { get; init; }
}

internal sealed record DashboardAction(int Id, string Action, string Target, string Reason, DateTimeOffset At);

internal sealed record DashboardAuditFlag(string Plugin, string Kind, string Note)
{
    /// <summary>Gets the audit issue's URL.</summary>
    public string? Url { get; init; }
}

internal sealed record DashboardReports(string Plugin, int Open);
