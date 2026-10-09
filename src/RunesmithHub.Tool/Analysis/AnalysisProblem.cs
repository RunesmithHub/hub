namespace RunesmithHub.Tool.Analysis;

/// <summary>One thing the analysis found, with a stable code such as <c>capability-undeclared</c>.</summary>
/// <param name="Location">The file or member it was found in, when there is one.</param>
internal sealed record AnalysisProblem(string Code, ProblemSeverity Severity, string Message, string? Location = null)
{
    public static AnalysisProblem Error(string code, string message, string? location = null) => new(code, ProblemSeverity.Error, message, location);

    public static AnalysisProblem Review(string code, string message, string? location = null) => new(code, ProblemSeverity.Review, message, location);

    public static AnalysisProblem Warning(string code, string message, string? location = null) => new(code, ProblemSeverity.Warning, message, location);
}
