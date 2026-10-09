using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Protocol.Index;

/// <summary>What a moderation entry targets.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<ModerationTargetKind>))]
public enum ModerationTargetKind
{
    Publisher,
    Plugin,

    /// <summary>A range of one plugin's versions.</summary>
    Versions,
}
