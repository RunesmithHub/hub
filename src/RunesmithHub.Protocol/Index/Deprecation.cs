namespace RunesmithHub.Protocol.Index;

/// <summary>Why a plugin is no longer maintained, and optionally what replaces it.</summary>
public sealed record Deprecation
{
    public required string Reason { get; init; }

    /// <summary>Gets the ID of the plugin that replaces it.</summary>
    public string? Replacement { get; init; }
}
