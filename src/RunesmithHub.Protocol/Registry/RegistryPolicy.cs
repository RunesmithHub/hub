using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Registry;

/// <summary>The registry's <c>policy/policy.json</c>.</summary>
public sealed record RegistryPolicy
{
    public const int DefaultHoldHours = 24;

    /// <summary>Gets the oldest Runesmith release that may use the index, or null when any may.</summary>
    public SemanticVersion? OldestRunesmith { get; init; }

    // Source-generated JSON passes null or zero for omitted init-only properties, so the setters restore the defaults.
    /// <summary>Gets other index base URLs, each ending in <c>/</c>.</summary>
    public IReadOnlyList<string> Mirrors { get; init => field = value ?? []; } = [];

    /// <summary>Gets how many hours a version waits before publishing when it needs no review; zero or omitted means 24.</summary>
    public int HoldHours { get; init => field = value == 0 ? DefaultHoldHours : value; } = DefaultHoldHours;
}
