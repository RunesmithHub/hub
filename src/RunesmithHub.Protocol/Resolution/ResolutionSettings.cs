using RunesmithHub.Protocol.Catalog;

namespace RunesmithHub.Protocol.Resolution;

/// <summary>The Runesmith release and user settings a resolution must respect.</summary>
public sealed record ResolutionSettings(RunesmithHost Host)
{
    public AllowedTiers AllowedTiers { get; init; } = AllowedTiers.All;

    /// <summary>Gets the plugins the user opted into pre-releases for.</summary>
    public IReadOnlySet<string> PreReleaseOptIns { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}
