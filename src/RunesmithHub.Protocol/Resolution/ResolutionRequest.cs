using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Resolution;

/// <summary>A user's request to the resolver. Create one with the factory methods.</summary>
public sealed record ResolutionRequest
{
    private ResolutionRequest(RequestKind kind, string? pluginId, SemanticVersion? version, IReadOnlyList<string> removedIds)
    {
        Kind = kind;
        PluginId = pluginId;
        Version = version;
        RemovedIds = removedIds;
    }

    public RequestKind Kind { get; }

    /// <summary>Gets the plugin to install or update.</summary>
    public string? PluginId { get; }

    /// <summary>Gets the exact version to install, or null for the best one.</summary>
    public SemanticVersion? Version { get; }

    /// <summary>Gets the plugins to remove.</summary>
    public IReadOnlyList<string> RemovedIds { get; }

    public static ResolutionRequest Install(string pluginId, SemanticVersion? version = null) => new(RequestKind.Install, pluginId, version, []);

    public static ResolutionRequest Update(string pluginId) => new(RequestKind.Update, pluginId, null, []);

    public static ResolutionRequest UpdateAll() => new(RequestKind.UpdateAll, null, null, []);

    public static ResolutionRequest Remove(params IEnumerable<string> pluginIds) => new(RequestKind.Remove, null, null, [.. pluginIds]);

    public static ResolutionRequest Sync() => new(RequestKind.Sync, null, null, []);
}
