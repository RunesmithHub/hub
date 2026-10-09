using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Protocol.Registry;

/// <summary>A plugin's state in the registry.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<RegistrationState>))]
public enum RegistrationState
{
    /// <summary>Registered; no version is published yet.</summary>
    Registered,
    Listed,
    Deprecated,
    Frozen,
    Blocked,
    Deleted,
}
