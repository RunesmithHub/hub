using RunesmithHub.Tool.Analysis.Heuristics;
using RunesmithHub.Tool.Analysis.Reporting;

namespace RunesmithHub.Tool.Analysis;

/// <summary>The public report of an analysis, the heuristic matches that only administrators see, and the SBOM of the verified package.</summary>
/// <param name="HeuristicMatches">The matches, or null when the heuristics were skipped.</param>
/// <param name="Sbom">The SBOM, or null when the package or its source could not be read.</param>
internal sealed record AnalysisResult(AnalysisReport Report, IReadOnlyList<HeuristicMatch>? HeuristicMatches, byte[]? Sbom);
