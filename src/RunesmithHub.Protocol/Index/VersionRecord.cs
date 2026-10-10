using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Index;

/// <summary>Everything the index says about one version of a plugin.</summary>
public record VersionRecord
{
    public required SemanticVersion Version { get; init; }

    public required VersionState State { get; init; }

    public required VersionReview Review { get; init; }

    /// <summary>Gets the full SHA of the commit the version was built from.</summary>
    public required string Commit { get; init; }

    public required string Tag { get; init; }

    public required GitHubAccount ReleaseAuthor { get; init; }

    /// <summary>Gets the package; absent until the version is published.</summary>
    public HostedFile? Package { get; init; }

    /// <summary>Gets the archive of the exact source the package was built from.</summary>
    public HostedFile? Source { get; init; }

    public HostedFile? Sbom { get; init; }

    public required ContractsPackage Contracts { get; init; }

    /// <summary>Gets the normalised Runesmith API range.</summary>
    public required VersionRange RunesmithApi { get; init; }

    /// <summary>Gets the dependencies with normalised ranges.</summary>
    public required IReadOnlyList<PluginDependency> Dependencies { get; init; }

    public required CapabilityReport Capabilities { get; init; }

    public required IReadOnlyList<string> NetworkHosts { get; init; }

    /// <summary>Gets the platforms the version supports; empty means every platform.</summary>
    public required IReadOnlyList<string> Platforms { get; init; }

    public required AnalysisSummary Analysis { get; init; }

    public required BuildSummary Build { get; init; }

    /// <summary>Gets a link to the public build attestation.</summary>
    public string? Attestation { get; init; }

    /// <summary>Gets the release notes as plain text, at most 10,000 characters.</summary>
    public required string ReleaseNotes { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Gets when a held version's hold ends.</summary>
    public DateTimeOffset? HoldUntil { get; init; }

    public required string Summary { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<string> Categories { get; init; }

    public required IReadOnlyList<string> Keywords { get; init; }

    public Deprecation? Deprecated { get; init; }

    /// <summary>Gets the SPDX license expression from the version's manifest.</summary>
    public string? License { get; init; }

    /// <summary>Gets the link to the version's build and analysis report.</summary>
    public string? ReportUrl { get; init; }

    /// <summary>Gets the developer's reason for yanking the version, while it is yanked.</summary>
    public string? YankReason { get; init; }

    /// <summary>Gets the BCP 47 tags of the languages of the plugin's own translations, English included; null when it is not localized.</summary>
    public IReadOnlyList<string>? Languages { get; init; }

    /// <summary>Gets the language the version adds as a language pack and what it translates; null when it is not a language pack.</summary>
    public LanguagePackSummary? LanguagePack { get; init; }

    /// <summary>Gets whether the version supports a platform.</summary>
    public bool SupportsPlatform(string platform) => Platforms.Count == 0 || Platforms.Contains(platform, StringComparer.Ordinal);
}
