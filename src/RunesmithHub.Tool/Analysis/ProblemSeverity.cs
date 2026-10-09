using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Tool.Analysis;

/// <summary>What a problem the analysis found means for the version.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<ProblemSeverity>))]
internal enum ProblemSeverity
{
    /// <summary>The version fails.</summary>
    Error,

    /// <summary>A person must look before the version is published.</summary>
    Review,

    /// <summary>Shown to reviewers and the developer; changes nothing.</summary>
    Warning,
}
