using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Catalog;

/// <summary>The Runesmith release plugins must run on.</summary>
/// <param name="ApiVersion">The API version the release provides, such as <c>0.4.2</c>.</param>
/// <param name="OldestApiVersion">The oldest API version it still runs plugins for, the start of its line, such as <c>0.4.0</c>.</param>
/// <param name="Platform">The platform name from <see cref="Platforms"/>, or null to ignore platforms.</param>
public sealed record RunesmithHost(SemanticVersion ApiVersion, SemanticVersion OldestApiVersion, string? Platform)
{
    /// <summary>Gets whether a plugin built for the API range runs here: the range holds the API version, and its minimum is not older than the oldest supported.</summary>
    public bool SupportsApi(VersionRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return range.Contains(ApiVersion) && range.Minimum >= OldestApiVersion;
    }

    public bool SupportsPlatform(VersionRecord version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return Platform is null || version.SupportsPlatform(Platform);
    }

    /// <summary>Gets whether a version runs on this release and platform.</summary>
    public bool CanRun(VersionRecord version) => SupportsApi(version.RunesmithApi) && SupportsPlatform(version);
}
