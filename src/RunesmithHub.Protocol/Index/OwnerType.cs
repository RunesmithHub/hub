using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Index;

/// <summary>The kind of GitHub account that owns a publisher.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OwnerType>))]
public enum OwnerType
{
    [JsonStringEnumMemberName("User")]
    User,

    [JsonStringEnumMemberName("Organization")]
    Organization,
}
