using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Protocol.Index;

/// <summary>What a moderation entry does.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<ModerationAction>))]
public enum ModerationAction
{
    Freeze,
    Block,
}
