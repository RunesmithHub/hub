using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Protocol.Index;

/// <summary>The outcome of a version's analysis.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<AnalysisVerdict>))]
public enum AnalysisVerdict
{
    Passed,

    /// <summary>The analysis passed but a review trigger applied.</summary>
    Review,
}
