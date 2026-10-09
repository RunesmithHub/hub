namespace RunesmithHub.Tool.Fixture;

/// <summary>Install counts per plugin, <c>stats.json</c>: unsigned and informational.</summary>
internal sealed record InstallStats(DateTimeOffset GeneratedAt, IReadOnlyDictionary<string, int> Installs);
