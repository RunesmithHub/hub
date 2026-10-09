namespace RunesmithHub.Tool.Analysis.Heuristics;

/// <summary>A heuristic rule that matched, and where. Only ever written to the private findings.</summary>
/// <param name="File">The package file it was found in.</param>
/// <param name="Location">The member or resource it was found in.</param>
/// <param name="Detail">A short excerpt of what matched.</param>
internal sealed record HeuristicMatch(string Rule, string File, string Location, string Detail);

/// <summary><c>private-findings.json</c>: details for administrators that are never published.</summary>
internal sealed record PrivateFindings(IReadOnlyList<HeuristicMatch> Heuristics)
{
    public const string FileName = "private-findings.json";
}
