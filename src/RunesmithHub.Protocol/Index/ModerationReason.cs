using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Protocol.Index;

/// <summary>The category of a moderation entry.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<ModerationReason>))]
public enum ModerationReason
{
    Malicious,
    CompromisedAccount,
    Vulnerability,
    Broken,
    Policy,
    Legal,
    DeveloperRequest,
}
