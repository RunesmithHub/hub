using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Resolution;

/// <summary>A hub plugin that is installed.</summary>
/// <param name="Requested">Whether the user asked for it, rather than it coming in as a dependency.</param>
public sealed record InstalledPlugin(string Id, SemanticVersion Version, bool Requested);
