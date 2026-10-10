namespace RunesmithHub.Tool.Build;

/// <summary>What the build workflow restores and builds, printed by <c>rshub build prepare</c> as JSON.</summary>
/// <param name="DotnetSdk">The .NET SDK version to build with.</param>
/// <param name="RunesmithSdk">The Runesmith SDK version the plugin builds against: the minimum of its API range.</param>
/// <param name="NuGetConfig">The hub's NuGet configuration to restore with.</param>
/// <param name="Properties">The MSBuild response file to pass to restore and build.</param>
/// <param name="PropertyArguments">The same properties as separate command line arguments.</param>
/// <param name="SourceArchive">The archive of the release commit's tree, written before anything of the plugin ran, or null without a commit.</param>
internal sealed record BuildPlan(
    string Id,
    string Version,
    string Commit,
    string PluginRoot,
    BuildProjects Projects,
    string DotnetSdk,
    string RunesmithSdk,
    string NuGetConfig,
    string Properties,
    IReadOnlyList<string> PropertyArguments,
    string? SourceArchive);

/// <summary>The absolute paths of a plugin's two project files.</summary>
internal sealed record BuildProjects(string Contracts, string Implementation);
