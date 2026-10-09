using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Index;

/// <summary>The hub's policy as clients need it. Pinned by the snapshot.</summary>
public sealed record PolicyPayload : IndexDocument
{
    public required IReadOnlyList<string> Categories { get; init; }

    /// <summary>Gets the description users see for each capability ID.</summary>
    public required IReadOnlyDictionary<string, CapabilityLabel> Capabilities { get; init; }

    /// <summary>Gets the oldest Runesmith release that may use this index, or null when any may.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required SemanticVersion? OldestRunesmith { get; init; }

    /// <summary>Gets other index base URLs, each ending in <c>/</c>.</summary>
    public required IReadOnlyList<string> Mirrors { get; init; }

    /// <summary>Gets how long a version that needs no review is held before it is published.</summary>
    public required int HoldHours { get; init; }
}
