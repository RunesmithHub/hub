using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Discovery;

/// <summary>The build jobs as a GitHub Actions matrix: <c>{ "include": [ ... ] }</c>.</summary>
internal sealed record BuildMatrix(IReadOnlyList<BuildJob> Include);
