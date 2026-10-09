using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Analysis.Heuristics;

namespace RunesmithHub.Tool.Analysis;

/// <summary>The plugin's repository checked out at the release commit, read only as data.</summary>
internal sealed record AnalysisSource(string Repository, string Commit);

/// <summary>What <see cref="PluginAnalyzer"/> analyses.</summary>
/// <param name="Previous">The previous published version's package, or null for a first version.</param>
/// <param name="Source">The source the package's lock files and build logic are read from.</param>
/// <param name="Rules">The heuristic rules, or null to skip the heuristics.</param>
internal sealed record AnalysisRequest(string Package, string? Previous, Registration Registration, AnalysisSource Source, HeuristicRuleSet? Rules);
