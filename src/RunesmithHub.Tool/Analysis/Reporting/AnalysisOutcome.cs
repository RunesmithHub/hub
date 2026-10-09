using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Tool.Analysis.Reporting;

/// <summary>The verdict of an analysis. Failed versions never reach the index, so its own verdict type has no <c>failed</c>.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<AnalysisOutcome>))]
internal enum AnalysisOutcome
{
    Passed,

    /// <summary>Every check passed but a review trigger applies.</summary>
    Review,

    Failed,
}

/// <summary>Whether the malware heuristics ran; without a rules file they are skipped.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<HeuristicsState>))]
internal enum HeuristicsState
{
    Skipped,
    Ran,
}
