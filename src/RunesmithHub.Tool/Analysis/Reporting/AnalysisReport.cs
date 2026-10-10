using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Analysis.Api;
using RunesmithHub.Tool.Analysis.Detection;
using RunesmithHub.Tool.Analysis.Vulnerabilities;
using RunesmithHub.Tool.Json;

namespace RunesmithHub.Tool.Analysis.Reporting;

/// <summary>
/// <c>analysis.json</c>: the analysis output the version record is written from. When the package can't be read, only the identity, verdict
/// and problems are present.
/// </summary>
internal sealed record AnalysisReport
{
    public const string FileName = "analysis.json";

    public int FormatVersion { get; init; } = 1;

    public required string Id { get; init; }

    public required DateTimeOffset AnalyzedAt { get; init; }

    public required AnalysisOutcome Verdict { get; init; }

    /// <summary>Gets the review trigger codes in <see cref="ReviewTriggers.All"/> order.</summary>
    public required IReadOnlyList<string> Triggers { get; init; }

    public required IReadOnlyList<AnalysisProblem> Problems { get; init; }

    public AnalysedPackage? Package { get; init; }

    public PreviousPackage? Previous { get; init; }

    /// <summary>Gets the packaged manifest: the listing fields, normalised ranges, assemblies and build of the version.</summary>
    public PackagedManifest? Manifest { get; init; }

    public ContractsPackage? Contracts { get; init; }

    public CapabilityAnalysis? Capabilities { get; init; }

    /// <summary>Gets the changes to the contracts since the previous version; absent for a first version.</summary>
    public ApiAnalysis? Api { get; init; }

    public DependencyAnalysis? Dependencies { get; init; }

    public IReadOnlyList<PackageAdvisories>? Vulnerabilities { get; init; }

    /// <summary>Gets the custom build logic in the source; absent when the source was not given.</summary>
    public IReadOnlyList<BuildLogicFinding>? BuildLogic { get; init; }

    /// <summary>Gets the files web views show; absent when neither this nor the previous version has any.</summary>
    public WebContentAnalysis? Web { get; init; }

    public HeuristicsState Heuristics { get; init; }

    /// <summary>Gets the SBOM the analysis wrote from the verified package; the build's SBOM must be exactly this file.</summary>
    public ProducedFile? Sbom { get; init; }

    /// <summary>Reads an <c>analysis.json</c> this tool wrote.</summary>
    /// <exception cref="ToolException">The file is not an analysis this tool can read.</exception>
    public static AnalysisReport Read(ReadOnlySpan<byte> json)
    {
        AnalysisReport report;
        try
        {
            report = ArtifactJson.Deserialize<AnalysisReport>(json);
        }
        catch (InvalidDataException exception)
        {
            throw new ToolException($"{FileName}: {exception.Message}", exception);
        }

        return report.FormatVersion == 1 ? report : throw new ToolException($"{FileName} has format version {report.FormatVersion}; this tool reads version 1.");
    }
}

/// <summary>The package file analysed.</summary>
internal sealed record AnalysedPackage(SemanticVersion Version, string Sha256, long Length);

/// <summary>A file the analysis wrote.</summary>
internal sealed record ProducedFile(string Sha256, long Length);

/// <summary>The previous published version the analysis compared with.</summary>
internal sealed record PreviousPackage(SemanticVersion Version, string Commit, long Length);

/// <summary>A detected capability with up to <see cref="CapabilityAnalysis.MaxCallSites"/> of the places it is used, and how many there are.</summary>
internal sealed record DetectedCapability(string Id, IReadOnlyList<CallSite> CallSites, int CallSiteCount);

/// <summary>Declared and detected capabilities, and which the version gains or loses against the previous one, with its new network hosts.</summary>
internal sealed record CapabilityAnalysis(
    IReadOnlyList<DeclaredCapability> Declared, IReadOnlyList<DetectedCapability> Detected, IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
{
    public const int MaxCallSites = 25;

    /// <summary>Gets the network hosts the version declares that the previous version did not.</summary>
    public IReadOnlyList<string> NetworkHostsAdded { get; init => field = value ?? []; } = [];
}

/// <summary>The contracts changes since the previous version.</summary>
internal sealed record ApiAnalysis(SemanticVersion PreviousVersion, IReadOnlyList<ApiChange> Breaking, IReadOnlyList<ApiChange> Additions);

/// <summary>A NuGet package of the version, from its SBOM.</summary>
internal sealed record ReportedPackage(string Id, string Version, bool Bundled, string? License);

/// <summary>A plugin dependency or NuGet package added, removed or changed since the previous version.</summary>
/// <param name="Kind"><c>plugin</c> or <c>package</c>.</param>
/// <param name="Change"><c>added</c>, <c>removed</c> or <c>changed</c>; a plugin dependency changes when it moves to another line.</param>
internal sealed record DependencyChange(string Kind, string Id, string Change, string? From, string? To);

/// <summary>The plugin dependencies and NuGet packages of the version, and their changes.</summary>
internal sealed record DependencyAnalysis(IReadOnlyList<PluginDependency> Plugins, IReadOnlyList<ReportedPackage> Packages, IReadOnlyList<DependencyChange> Changes);

/// <summary>The files under <c>web/</c> that web views show.</summary>
/// <param name="Code">The JavaScript and WebAssembly files.</param>
/// <param name="Changed">The web files added, changed or removed since the previous version; null for a first version.</param>
internal sealed record WebContentAnalysis(int Files, IReadOnlyList<string> Code, IReadOnlyList<string>? Changed);
