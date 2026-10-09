using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Tool.Analysis.Api;

/// <summary>
/// Checks that a version's number fits the changes to its contracts since the previous version: within one compatibility line, nothing may
/// break, and from 1.0 on additions need a new minor version. Before 1.0 each minor version is its own line, so additions fit any patch.
/// Pre-releases are not checked.
/// </summary>
internal static class VersionBumpCheck
{
    public static IReadOnlyList<AnalysisProblem> Check(SemanticVersion previous, SemanticVersion current, ApiDiff diff)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(diff);
        if (current.IsPreRelease || previous.Line != current.Line)
            return [];

        var problems = new List<AnalysisProblem>();
        if (diff.Breaking.Count > 0)
        {
            var next = current.Line.Next.Start;
            problems.Add(AnalysisProblem.Error(
                ProblemCodes.ApiBreaking,
                $"The contracts remove or change {diff.Breaking.Count} public members since {previous}, which breaks dependents; release it as {next} or later."));
        }

        if (diff.Additions.Count > 0 && current.Major > 0 && current.Minor == previous.Minor)
        {
            problems.Add(AnalysisProblem.Error(
                ProblemCodes.ApiAddition,
                $"The contracts add {diff.Additions.Count} public members since {previous}, which needs a new minor version; release it as {current.Major}.{current.Minor + 1}.0 or later."));
        }

        return problems;
    }
}
