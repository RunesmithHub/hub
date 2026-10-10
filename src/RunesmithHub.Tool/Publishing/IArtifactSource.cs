using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Tool.Publishing;

/// <summary>The artifacts the build workflow uploads for each version, named for the plugin and version because one run can build several.</summary>
internal static class WorkflowArtifacts
{
    /// <summary>The first build's output: package, contracts package, source archive, SBOM and images.</summary>
    public static string Package(string plugin, SemanticVersion version) => $"package-{plugin}-{version}";

    /// <summary>The public analysis: <c>analysis.json</c>, <c>summary.md</c> and <c>build-info.json</c>.</summary>
    public static string Analysis(string plugin, SemanticVersion version) => $"analysis-{plugin}-{version}";
}

/// <summary>Gets workflow artifacts of the registry's runs.</summary>
internal interface IArtifactSource
{
    /// <summary>Downloads a run's artifact and extracts it into a folder.</summary>
    /// <exception cref="ToolException">The run has no such artifact, or it expired.</exception>
    Task DownloadAsync(long runId, string name, string directory, CancellationToken cancellationToken);
}
