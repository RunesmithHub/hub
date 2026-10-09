namespace RunesmithHub.Tool.Check;

/// <summary>What <see cref="DeveloperCheck"/> checks and where it writes its files.</summary>
/// <param name="PluginRoot">The plugin's folder, with <c>plugin.json</c>.</param>
/// <param name="Output">A folder the check owns; its <c>build</c>, <c>package</c> and <c>analysis</c> folders are replaced.</param>
/// <param name="Build">Whether to restore and build first; false analyses the existing Release output.</param>
internal sealed record CheckRequest(string PluginRoot, string Output, bool Build, Uri HubFeed);
