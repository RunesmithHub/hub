using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Index;

/// <summary>A dependency on another plugin, with its normalised range.</summary>
public sealed record PluginDependency
{
    public required string Id { get; init; }

    public required VersionRange Range { get; init; }
}
