using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Index;

/// <summary>The key IDs allowed to sign for a role, and how many distinct ones must.</summary>
public sealed record RoleKeys
{
    [JsonPropertyName("keyids")]
    public required IReadOnlyList<string> KeyIds { get; init; }

    public required int Threshold { get; init; }

    /// <summary>Gets whether two roles have the same keys and threshold.</summary>
    public bool HasSameKeys(RoleKeys other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Threshold == other.Threshold && KeyIds.ToHashSet(StringComparer.Ordinal).SetEquals(other.KeyIds);
    }
}
