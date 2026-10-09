using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Protocol.Registry;

/// <summary>A version's registry file, <c>plugins/&lt;id&gt;/versions/&lt;version&gt;.json</c>: the version record and fields only the registry keeps.</summary>
public sealed record RegistryVersionRecord : VersionRecord
{
    /// <summary>Gets the number of the version's tracking issue.</summary>
    [JsonPropertyOrder(100)]
    public required int TrackingIssue { get; init; }

    /// <summary>Gets the ID of the workflow run that built the version.</summary>
    [JsonPropertyOrder(101)]
    public required long BuildRun { get; init; }

    /// <summary>Gets the SHA-256 of the analysis output.</summary>
    [JsonPropertyOrder(102)]
    public required string AnalysisDigest { get; init; }

    /// <summary>Gets the images and contracts package published with the version.</summary>
    [JsonPropertyOrder(103)]
    public VersionAssets? Assets { get; init; }

    /// <summary>Returns the version record as the index carries it, without the registry-only fields.</summary>
    public VersionRecord ToIndexRecord() => new()
    {
        Version = Version,
        State = State,
        Review = Review,
        Commit = Commit,
        Tag = Tag,
        ReleaseAuthor = ReleaseAuthor,
        Package = Package,
        Source = Source,
        Sbom = Sbom,
        Contracts = Contracts,
        RunesmithApi = RunesmithApi,
        Dependencies = Dependencies,
        Capabilities = Capabilities,
        NetworkHosts = NetworkHosts,
        Platforms = Platforms,
        Analysis = Analysis,
        Build = Build,
        Attestation = Attestation,
        ReleaseNotes = ReleaseNotes,
        PublishedAt = PublishedAt,
        HoldUntil = HoldUntil,
        Summary = Summary,
        Name = Name,
        Categories = Categories,
        Keywords = Keywords,
        Deprecated = Deprecated,
        License = License,
        ReportUrl = ReportUrl,
        YankReason = YankReason,
    };
}
