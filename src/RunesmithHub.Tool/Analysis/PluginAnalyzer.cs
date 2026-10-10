using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Tool.Analysis.Api;
using RunesmithHub.Tool.Analysis.Detection;
using RunesmithHub.Tool.Analysis.Heuristics;
using RunesmithHub.Tool.Analysis.Isolation;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Analysis.Vulnerabilities;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Analysis;

/// <summary>
/// Runs every check of the hub's analysis over a built package and the previous published version. Everything in the package is untrusted
/// data: assemblies are read as metadata and never loaded or run.
/// </summary>
internal sealed class PluginAnalyzer(IVulnerabilityDatabase vulnerabilities, INuGetPackageSource packages, TimeProvider time)
{
    private const int SizeGrowthFactor = 5;

    public async Task<AnalysisResult> AnalyseAsync(AnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limits = PackageLimits.From(request.Registration.Limits);
        PackageSnapshot package;
        try
        {
            package = PackageSnapshot.Open(request.Package, limits);
        }
        catch (PackageException exception)
        {
            var problems = exception.Problems.Select(problem => AnalysisProblem.Error(ProblemCodes.PackageRules, problem)).ToList();
            return new AnalysisResult(Failed(request.Registration.Id, problems, Now()), null, null);
        }

        using (package)
        {
            using var previous = request.Previous is { } path ? OpenPrevious(path, limits) : null;
            return await AnalyseAsync(request, package, previous, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<AnalysisResult> AnalyseAsync(AnalysisRequest request, PackageSnapshot package, PackageSnapshot? previous, CancellationToken cancellationToken)
    {
        var manifest = package.Manifest;
        var problems = new List<AnalysisProblem>(package.Problems);
        problems.AddRange(PackagedManifestCheck.Check(manifest, request.Registration));

        var detected = CapabilityScanner.Scan(package.Assemblies, package.NativeLibraries);
        var previousDetected = previous is null ? null : CapabilityScanner.Scan(previous.Assemblies, previous.NativeLibraries);
        var (capabilities, capabilityProblems) = CapabilityCheck.Check(manifest, detected, previous?.Manifest, previousDetected);
        problems.AddRange(capabilityProblems);
        problems.AddRange(IsolationCheck.Check(package));

        var api = CompareApi(package, previous, problems);
        problems.AddRange(SdkReferenceCheck.Check(package));
        var provenance = await new BundleProvenance(packages).CheckAsync(package, request.Registration, request.Source, cancellationToken).ConfigureAwait(false);
        problems.AddRange(provenance.Problems);
        var components = provenance.Components;

        var dependencies = DependencyComparison.Compare(manifest, components, previous);
        var advisories = components.Count == 0 ? [] : await vulnerabilities.QueryAsync(components, cancellationToken).ConfigureAwait(false);
        problems.AddRange(advisories.Select(advisory => AnalysisProblem.Error(
            ProblemCodes.Vulnerability, $"{advisory.Id} {advisory.Version} has known vulnerabilities: {string.Join(", ", advisory.Advisories)}. Update it.", advisory.Id)));
        problems.AddRange(LicenseCheck.Check(manifest.License, components));

        var buildLogic = BuildLogicScanner.Scan(request.Source.Repository);
        if (buildLogic.Count > 0)
            problems.Add(AnalysisProblem.Review(ProblemCodes.BuildLogic, $"The project files add build logic: {string.Join("; ", buildLogic.Select(finding => $"{finding.Kind} {finding.Name} in {finding.File}"))}."));

        var sizeGrowth = previous is not null && package.Length > previous.Length * SizeGrowthFactor;
        if (sizeGrowth)
            problems.Add(AnalysisProblem.Review(ProblemCodes.SizeGrowth, $"The package grew from {previous!.Length} to {package.Length} bytes since {previous.Manifest.Version}."));

        var (web, webProblem) = WebContentCheck.Check(package, previous);
        if (webProblem is not null)
            problems.Add(webProblem);

        var matches = request.Rules is { } rules ? HeuristicEngine.Run(rules, package) : null;
        if (matches is { Count: > 0 })
            problems.Add(AnalysisProblem.Review(ProblemCodes.Heuristic, "A review rule matched; an administrator will look at the details."));

        var triggers = ReviewTriggerRules.Triggers(new TriggerFacts
        {
            FirstVersion = previous is null,
            Capabilities = CapabilityCheck.Union(manifest.Capabilities.Select(capability => capability.Id), detected),
            CapabilitiesAdded = previous is null ? [] : capabilities.Added,
            NewPackages = BundlesNewPackage(components, previous),
            HeuristicMatched = matches is { Count: > 0 },
            BuildLogic = buildLogic.Count > 0,
            SizeGrowth = sizeGrowth,
            NameChanged = previous is not null && !string.Equals(previous.Manifest.Name, manifest.Name, StringComparison.Ordinal),
            NetworkHostsAdded = capabilities.NetworkHostsAdded.Count > 0,
            Tier = request.Registration.Tier,
            Problems = problems,
        });

        var report = new AnalysisReport
        {
            Id = manifest.Id,
            AnalyzedAt = Now(),
            Verdict = Verdict(problems, triggers),
            Triggers = triggers,
            Problems = problems,
            Package = new AnalysedPackage(manifest.Version, package.Sha256, package.Length),
            Previous = previous is null ? null : new PreviousPackage(previous.Manifest.Version, previous.Manifest.Build.Commit, previous.Length),
            Manifest = manifest,
            Contracts = Contracts(package),
            Capabilities = capabilities,
            Api = api,
            Dependencies = dependencies,
            Vulnerabilities = advisories,
            BuildLogic = buildLogic,
            Web = web,
            Heuristics = request.Rules is null ? HeuristicsState.Skipped : HeuristicsState.Ran,
            Sbom = provenance.Sbom is { } sbom ? new ProducedFile(Hashes.Sha256(sbom), sbom.Length) : null,
        };
        return new AnalysisResult(report, matches, provenance.Sbom);
    }

    private static bool BundlesNewPackage(IReadOnlyList<SbomComponent> components, PackageSnapshot? previous)
    {
        var before = (previous?.Components ?? []).Where(component => component.Bundled).Select(component => component.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return components.Any(component => component.Bundled && !before.Contains(component.Id));
    }

    private static PackageSnapshot OpenPrevious(string path, PackageLimits limits)
    {
        try
        {
            return PackageSnapshot.Open(path, limits);
        }
        catch (PackageException exception)
        {
            throw new ToolException($"The previous version's package {path} can't be read: {exception.Message}", exception);
        }
    }

    private static ApiAnalysis? CompareApi(PackageSnapshot package, PackageSnapshot? previous, List<AnalysisProblem> problems)
    {
        if (previous?.Contracts is not { } before || package.Contracts is not { } after)
            return null;
        var diff = ApiDiff.Compare(ApiSurface.Read(before.Definition), ApiSurface.Read(after.Definition));
        problems.AddRange(VersionBumpCheck.Check(previous.Manifest.Version, package.Manifest.Version, diff));
        return new ApiAnalysis(previous.Manifest.Version, diff.Breaking, diff.Additions);
    }

    private static ContractsPackage? Contracts(PackageSnapshot package) => package.Contracts is null
        ? null
        : new ContractsPackage
        {
            Assembly = package.Manifest.Assemblies.Contracts.File,
            Sha256 = package.Manifest.Assemblies.Contracts.Sha256,
            Package = ContractsNuGetPackage.Id(package.Manifest.Assemblies.Contracts.File),
            PackageVersion = package.Manifest.Version.ToString(),
        };

    private static AnalysisOutcome Verdict(IReadOnlyList<AnalysisProblem> problems, IReadOnlyList<string> triggers) =>
        problems.Any(problem => problem.Severity == ProblemSeverity.Error) ? AnalysisOutcome.Failed
        : triggers.Count > 0 ? AnalysisOutcome.Review
        : AnalysisOutcome.Passed;

    private DateTimeOffset Now() => UtcTimestampConverter.Truncate(time.GetUtcNow());

    private static AnalysisReport Failed(string id, IReadOnlyList<AnalysisProblem> problems, DateTimeOffset analyzedAt) =>
        new() { Id = id, AnalyzedAt = analyzedAt, Verdict = AnalysisOutcome.Failed, Triggers = [], Problems = problems };
}
