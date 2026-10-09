namespace RunesmithHub.Protocol.Index;

/// <summary>The outcome of a version's analysis.</summary>
public sealed record AnalysisSummary
{
    public required AnalysisVerdict Verdict { get; init; }

    /// <summary>Gets the review trigger codes, such as <c>first-version</c>; see <see cref="ReviewTriggers"/>.</summary>
    public required IReadOnlyList<string> Triggers { get; init; }
}
