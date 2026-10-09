using System.Text;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Analysis;

/// <summary>The SBOM of a package's verified contents, its components, and the problems found on the way.</summary>
/// <param name="Sbom">The SBOM's bytes, or null when the source could not be read.</param>
internal sealed record ProvenanceResult(byte[]? Sbom, IReadOnlyList<SbomComponent> Components, IReadOnlyList<AnalysisProblem> Problems);

/// <summary>
/// Checks what the build says about where the package's files came from, trusting none of it: every file from a NuGet package must be byte
/// for byte a file in the package the plugin's lock files pin at the release commit, with the content hash they record. The SBOM and notices
/// are then written from what was verified and must equal the package's own.
/// </summary>
internal sealed class BundleProvenance(INuGetPackageSource packages)
{
    private const string LibFolder = "lib/";

    public async Task<ProvenanceResult> CheckAsync(PackageSnapshot package, Registration registration, AnalysisSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(source);
        var manifest = package.Manifest;
        var problems = new List<AnalysisProblem>();
        if (!string.Equals(manifest.Build.Commit, source.Commit, StringComparison.OrdinalIgnoreCase))
            problems.Add(AnalysisProblem.Error(ProblemCodes.Source, $"The package says it was built from {manifest.Build.Commit}, not the release commit {source.Commit}."));

        IReadOnlyList<LockedPackage> locked;
        try
        {
            var root = Path.GetFullPath(Path.Combine(source.Repository, registration.Repository.Path));
            CheckoutPaths.EnsureNoLinks(source.Repository, root);
            locked = PluginSource.Load(root, new PluginExpectations(manifest.Id, registration.Repository.Url, manifest.Version.ToString())).LockedPackages();
        }
        catch (ToolException exception)
        {
            problems.Add(AnalysisProblem.Error(ProblemCodes.Source, $"The source at the release commit can't be read: {exception.Message}"));
            return new ProvenanceResult(null, [], problems);
        }

        using var session = new Session(packages, locked, problems, cancellationToken);
        await session.VerifyAssembliesAsync(package).ConfigureAwait(false);
        await session.VerifyNativeLibrariesAsync(package).ConfigureAwait(false);
        var bundled = session.Bundled;

        var sbom = CycloneDxSbom.Create(manifest.Id, manifest.Version.ToString(), source.Commit, manifest.Repository, locked, bundled);
        CheckSame(package, CycloneDxSbom.FileName, sbom, problems);
        CheckSame(package, ThirdPartyNotices.FileName, Encoding.UTF8.GetBytes(ThirdPartyNotices.Create(manifest.Name, bundled)), problems);
        return new ProvenanceResult(sbom, SbomComponent.Read(sbom), problems);
    }

    private static void CheckSame(PackageSnapshot package, string file, byte[] expected, List<AnalysisProblem> problems)
    {
        if (package.Entries.Contains(file) && !package.ReadFile(file).AsSpan().SequenceEqual(expected))
            problems.Add(AnalysisProblem.Error(ProblemCodes.BundledProvenance, $"{file} in the package does not describe its verified contents.", file));
    }

    private sealed class Session(INuGetPackageSource packages, IReadOnlyList<LockedPackage> locked, List<AnalysisProblem> problems, CancellationToken cancellationToken) : IDisposable
    {
        private readonly Dictionary<LockedPackage, NuGetPackage?> _downloaded = [];
        private readonly Dictionary<LockedPackage, BundledPackage> _bundled = [];

        public IReadOnlyList<BundledPackage> Bundled => [.. _bundled.Values.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)];

        public async Task VerifyAssembliesAsync(PackageSnapshot package)
        {
            var fromPackages = package.Manifest.Assemblies.Bundled.Where(assembly => !assembly.IsBuiltFromRepository);
            foreach (var group in fromPackages.GroupBy(assembly => (Id: assembly.Package!.ToUpperInvariant(), Version: assembly.PackageVersion!.ToUpperInvariant())))
            {
                var first = group.First();
                var pinned = locked.FirstOrDefault(candidate => string.Equals(candidate.Id, first.Package, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.Version, first.PackageVersion, StringComparison.OrdinalIgnoreCase));
                if (pinned is null)
                {
                    foreach (var assembly in group)
                        problems.Add(AnalysisProblem.Error(ProblemCodes.BundledProvenance, $"lib/{assembly.File} names {first.Package} {first.PackageVersion}, which the lock files do not pin.", $"lib/{assembly.File}"));
                    continue;
                }

                if (await DownloadAsync(pinned).ConfigureAwait(false) is not { } nuget)
                    continue;
                foreach (var assembly in group)
                {
                    if (!nuget.HasFile(assembly.File, package.ReadFile(LibFolder + assembly.File)))
                        problems.Add(AnalysisProblem.Error(ProblemCodes.BundledProvenance, $"lib/{assembly.File} is not a file of {pinned.Id} {pinned.Version}.", $"lib/{assembly.File}"));
                }

                _bundled.TryAdd(pinned, BundledPackage.From(pinned, nuget));
            }
        }

        public async Task VerifyNativeLibrariesAsync(PackageSnapshot package)
        {
            foreach (var entry in package.NativeLibraries)
            {
                var content = package.ReadFile(entry);
                var ridAndFile = entry[PackageEntryRules.NativePrefix.Length..];
                var slash = ridAndFile.IndexOf('/', StringComparison.Ordinal);
                var pathInPackage = $"runtimes/{ridAndFile[..slash]}/native/{ridAndFile[(slash + 1)..]}";
                var source = await FindAsync(pathInPackage, content).ConfigureAwait(false);
                if (source is null)
                    problems.Add(AnalysisProblem.Error(ProblemCodes.BundledProvenance, $"{entry} is not {pathInPackage} of any package the lock files pin.", entry));
            }
        }

        private async Task<LockedPackage?> FindAsync(string pathInPackage, byte[] content)
        {
            foreach (var candidate in locked.OrderBy(candidate => _bundled.ContainsKey(candidate) ? 0 : 1))
            {
                if (await DownloadAsync(candidate).ConfigureAwait(false) is not { } nuget || nuget.ReadFile(pathInPackage) is not { } bytes || !bytes.AsSpan().SequenceEqual(content))
                    continue;
                _bundled.TryAdd(candidate, BundledPackage.From(candidate, nuget));
                return candidate;
            }

            return null;
        }

        private async Task<NuGetPackage?> DownloadAsync(LockedPackage pinned)
        {
            if (_downloaded.TryGetValue(pinned, out var known))
                return known;
            NuGetPackage? package = null;
            try
            {
                package = NuGetPackage.Open(await packages.GetAsync(pinned.Id, pinned.Version, cancellationToken).ConfigureAwait(false));
                if (!string.Equals(package.ContentHash, pinned.ContentHash, StringComparison.Ordinal))
                {
                    problems.Add(AnalysisProblem.Error(ProblemCodes.BundledProvenance, $"{pinned.Id} {pinned.Version} from the feed does not have the content hash its lock file records.", pinned.Id));
                    package.Dispose();
                    package = null;
                }
            }
            catch (ToolException exception)
            {
                problems.Add(AnalysisProblem.Error(ProblemCodes.BundledProvenance, exception.Message, pinned.Id));
            }

            _downloaded[pinned] = package;
            return package;
        }

        public void Dispose()
        {
            foreach (var package in _downloaded.Values)
                package?.Dispose();
        }
    }
}
