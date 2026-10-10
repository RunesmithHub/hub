using RunesmithHub.Protocol.Packaging;

namespace RunesmithHub.Tool.Build;

/// <summary>What <see cref="PackageAssembler"/> packages and where it writes the result.</summary>
/// <param name="PluginRoot">The plugin's folder, restored and built.</param>
/// <param name="Output">An empty or missing folder for the package and everything published beside it.</param>
/// <param name="Run">The URL of the pipeline run, or <c>local</c>.</param>
/// <param name="DotnetSdk">The .NET SDK version that built the plugin.</param>
/// <param name="SourceArchive">The source archive <c>rshub build prepare</c> wrote, or null for folders that are not Git repositories.</param>
internal sealed record PackageRequest(string PluginRoot, string Output, string Commit, string Run, string DotnetSdk, string? SourceArchive)
{
    public PackageLimits Limits { get; init; } = PackageLimits.Default;
}
