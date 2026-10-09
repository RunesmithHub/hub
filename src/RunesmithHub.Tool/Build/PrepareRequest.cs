namespace RunesmithHub.Tool.Build;

/// <summary>What <see cref="BuildPreparer"/> checks and where it writes the build's configuration.</summary>
/// <param name="RepositoryRoot">The checkout of the plugin's repository.</param>
/// <param name="PluginPath">The plugin's folder inside the repository, <c>.</c> for the root.</param>
/// <param name="Commit">The commit the checkout must be at, or null to skip the Git checks for a folder that is not a repository.</param>
internal sealed record PrepareRequest(
    string RepositoryRoot,
    string PluginPath,
    PluginExpectations Expectations,
    string? Commit,
    string Output,
    Uri HubFeed);
