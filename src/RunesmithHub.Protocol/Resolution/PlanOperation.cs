using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Resolution;

/// <summary>One step of an install plan.</summary>
/// <param name="Name">The plugin's display name.</param>
/// <param name="From">The installed version, or null when it is not installed.</param>
/// <param name="To">The version after the plan, or null when it is removed.</param>
/// <param name="Tier">The plugin's tier, or null when the index does not have the plugin.</param>
/// <param name="Record">The record of <paramref name="To"/>, for its package, size and capabilities.</param>
public sealed record PlanOperation(
    OperationKind Kind, string PluginId, string Name, SemanticVersion? From, SemanticVersion? To, PluginTier? Tier, OperationReason Reason,
    VersionRecord? Record);
