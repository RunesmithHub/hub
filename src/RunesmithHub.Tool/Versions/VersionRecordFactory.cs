using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Versions;

/// <summary>Everything a version record is computed from.</summary>
/// <param name="AnalysisDigest">The SHA-256 of the exact bytes of <c>analysis.json</c>.</param>
/// <param name="OutputDirectory">The folder <c>rshub build package</c> wrote.</param>
/// <param name="RunId">The ID of the workflow run that built and analysed the version.</param>
internal sealed record VersionInputs(AnalysisReport Analysis, string AnalysisDigest, BuildJob Job, string OutputDirectory, long RunId)
{
    /// <summary>Reads the analysis file and pairs it with the job and the build output.</summary>
    /// <exception cref="ToolException">The analysis can't be read.</exception>
    public static VersionInputs Read(byte[] analysis, BuildJob job, string outputDirectory, long runId) =>
        new(AnalysisReport.Read(analysis), Hashes.Sha256(analysis), job, outputDirectory, runId);
}

/// <summary>A computed version record and the packaged manifest it came from.</summary>
internal sealed record ComputedVersion(RegistryVersionRecord Record, PackagedManifest Manifest);

/// <summary>
/// Computes a version record from the analysis, the build job, the build output and the registry. The proposal and the verify gate both use
/// it, so a record is valid only when it is exactly what this produces.
/// </summary>
internal static class VersionRecordFactory
{
    public const int MaxReleaseNotes = 10_000;

    /// <exception cref="ToolException">The inputs disagree with each other or with the registry.</exception>
    public static async Task<ComputedVersion> CreateAsync(VersionInputs inputs, RegistryState registry, IRegistryHistory history, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(registry);
        var (analysis, digest, job, outputDirectory, runId) = inputs;
        if (analysis.Verdict == AnalysisOutcome.Failed)
            throw new ToolException("The analysis failed, so there is no version record to write.");

        var registration = registry.FindRegistration(job.Plugin) ?? throw new ToolException($"{job.Plugin} is not registered.");
        var publisher = registry.FindPublisher(PluginId.Parse(job.Plugin).Publisher) ?? throw new ToolException($"The publisher of {job.Plugin} is not registered.");
        var version = SemanticVersion.TryParse(job.Version, out var parsed) ? parsed : throw new ToolException($"\"{job.Version}\" is not a version.");
        if (!string.Equals(analysis.Id, job.Plugin, StringComparison.Ordinal) || analysis.Package?.Version != version)
            throw new ToolException($"The analysis is of {analysis.Id} {analysis.Package?.Version}, not {job.Plugin} {version}.");

        var output = BuildOutput.Open(outputDirectory, job.Plugin, version, registry.LimitsOf(registration), ExpectedHashes.FromHashList(outputDirectory));
        if (!string.Equals(analysis.Package.Sha256, output.Package.Sha256, StringComparison.Ordinal))
            throw new ToolException($"The analysis is of another build of {job.Plugin} {version}: the package hashes differ.");
        if (!string.Equals(analysis.Sbom?.Sha256, output.Sbom.Sha256, StringComparison.Ordinal))
            throw new ToolException($"The build's SBOM of {job.Plugin} {version} is not the one the analysis wrote from the verified package.");
        if (registry.VersionsOf(job.Plugin).Any(record => record.Version == version))
            throw new ToolException($"{job.Plugin} {version} already has a record.");

        var manifest = output.Manifest;
        var previous = registry.VersionsOf(job.Plugin).Where(record => record.Version < version).MaxBy(record => record.Version);
        var previousMaintainers = previous is null ? null : await history.MaintainersAtVersionAsync(job.Plugin, previous.Version, cancellationToken).ConfigureAwait(false);
        var triggers = RegistryReviewTriggers.Canonical(analysis.Triggers.Concat(
            RegistryReviewTriggers.For(registration, publisher, job.ReleaseAuthor, previous, previousMaintainers, registry)));
        var review = triggers.Count > 0 || analysis.Verdict == AnalysisOutcome.Review;

        string Url(string file) => HubLocations.ReleaseAssetUrl(job.Plugin, version, file);
        var record = new RegistryVersionRecord
        {
            Version = version,
            State = review ? VersionState.Review : VersionState.Held,
            Review = new VersionReview { State = ReviewState.Automated },
            Commit = job.Commit,
            Tag = job.Tag,
            ReleaseAuthor = job.ReleaseAuthor,
            Package = output.Package.Hosted(Url(output.Package.AssetName)),
            Source = output.Source.Hosted(Url(output.Source.AssetName)),
            Sbom = output.Sbom.Hosted(Url(output.Sbom.AssetName)),
            Contracts = new ContractsPackage
            {
                Assembly = manifest.Assemblies.Contracts.File,
                Sha256 = manifest.Assemblies.Contracts.Sha256,
                Package = output.ContractsPackageId,
                PackageVersion = version.ToString(),
            },
            RunesmithApi = manifest.RunesmithApi,
            Dependencies = manifest.Dependencies,
            Capabilities = new CapabilityReport
            {
                Declared = manifest.Capabilities,
                Detected = [.. (analysis.Capabilities?.Detected ?? []).Select(capability => capability.Id).Order(StringComparer.Ordinal)],
            },
            NetworkHosts = manifest.NetworkHosts,
            Platforms = manifest.Platforms,
            Analysis = new AnalysisSummary { Verdict = review ? AnalysisVerdict.Review : AnalysisVerdict.Passed, Triggers = triggers },
            Build = new BuildSummary { Run = HubLocations.RunUrl(runId), Sdk = manifest.Build.Sdk, Reproducible = true },
            ReleaseNotes = job.ReleaseNotes.Length > MaxReleaseNotes ? job.ReleaseNotes[..MaxReleaseNotes] : job.ReleaseNotes,
            HoldUntil = review ? null : analysis.AnalyzedAt.AddHours(registry.Policy.HoldHours),
            Summary = manifest.Summary,
            Name = manifest.Name,
            Categories = manifest.Categories,
            Keywords = manifest.Keywords,
            Deprecated = manifest.Deprecated,
            License = manifest.License,
            ReportUrl = HubLocations.IssueUrl(job.TrackingIssue),
            TrackingIssue = job.TrackingIssue,
            BuildRun = runId,
            AnalysisDigest = digest,
            Assets = Assets(output, Url),
        };
        return new ComputedVersion(record, manifest);
    }

    private static VersionAssets Assets(BuildOutput output, Func<string, string> url) => new()
    {
        Icon = new IconSet
        {
            Size64 = output.Icons[0].Hosted(url(output.Icons[0].AssetName)),
            Size128 = output.Icons[1].Hosted(url(output.Icons[1].AssetName)),
            Size256 = output.Icons[2].Hosted(url(output.Icons[2].AssetName)),
        },
        Banner = output.Banner is { } banner
            ? new BannerSet { Width640 = banner.Width640.Hosted(url(banner.Width640.AssetName)), Width1280 = banner.Width1280.Hosted(url(banner.Width1280.AssetName)) }
            : null,
        Gallery = [.. output.Gallery.Select(item => new GalleryItem
        {
            Caption = item.Caption,
            Width640 = item.Width640.Hosted(url(item.Width640.AssetName)),
            Width1280 = item.Width1280.Hosted(url(item.Width1280.AssetName)),
        })],
        ContractsPackage = output.ContractsPackage.Asset(),
    };
}
