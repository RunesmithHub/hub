using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Protocol.Index;

/// <summary>Whether a person has read a version.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<ReviewState>))]
public enum ReviewState
{
    /// <summary>Passed every automated check; no person has read it.</summary>
    Automated,

    /// <summary>An administrator read and approved it.</summary>
    Reviewed,
}
