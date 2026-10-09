using System.Globalization;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Versions;

/// <summary>
/// The checks of a new version that need the published index and the registry: its dependencies exist with a published version in range
/// that the plugin's tier may use, it closes no cycle and keeps its closure small, and no other plugin has assemblies with its names.
/// </summary>
internal static class DependencyChecks
{
    public const int MaxClosure = 32;

    /// <summary>Lists every problem, each a sentence; empty when the version passes.</summary>
    public static IReadOnlyList<string> Check(PackagedManifest manifest, Registration registration, RegistryState registry, HubCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(catalog);
        var problems = new List<string>();
        foreach (var dependency in manifest.Dependencies)
            problems.AddRange(CheckDependency(dependency, registration.Tier, catalog));
        problems.AddRange(CheckGraph(manifest, catalog));
        problems.AddRange(CheckAssemblyNames(manifest, registry));
        return problems;
    }

    private static IEnumerable<string> CheckDependency(PluginDependency dependency, PluginTier tier, HubCatalog catalog)
    {
        if (catalog.FindPlugin(dependency.Id) is not { } plugin)
        {
            yield return $"The dependency {dependency.Id} is not a plugin on the hub.";
            yield break;
        }

        if (!MayDependOn(tier, plugin.Tier))
        {
            yield return $"A {Name(tier)} plugin can't depend on {plugin.Name} ({plugin.Id}), which is {Name(plugin.Tier)}; it may depend only on {Allowed(tier)} plugins.";
            yield break;
        }

        var reviewedOnly = tier != PluginTier.Unverified;
        if (!plugin.Versions.Any(version => dependency.Range.Contains(version.Version) && catalog.GetEffectiveState(plugin, version).IsInstallable
            && (!reviewedOnly || version.Review.State == ReviewState.Reviewed)))
        {
            yield return $"No {(reviewedOnly ? "reviewed, " : "")}published version of {plugin.Name} ({plugin.Id}) satisfies {dependency.Range}.";
        }
    }

    /// <summary>Walks the dependencies through every published version in range, looking for the plugin itself and counting the closure.</summary>
    private static IEnumerable<string> CheckGraph(PackagedManifest manifest, HubCatalog catalog)
    {
        var reached = new Dictionary<string, List<VersionRange>>(StringComparer.Ordinal);
        var queue = new Queue<(string Id, VersionRange Range, string Path)>(manifest.Dependencies.Select(dependency => (dependency.Id, dependency.Range, $"{manifest.Id} -> {dependency.Id}")));
        var cycles = new List<string>();
        while (queue.TryDequeue(out var step))
        {
            if (string.Equals(step.Id, manifest.Id, StringComparison.Ordinal))
            {
                if (step.Range.Contains(manifest.Version, allowPreRelease: true))
                    cycles.Add(step.Path);
                continue;
            }

            if (reached.TryGetValue(step.Id, out var ranges) && ranges.Contains(step.Range))
                continue;
            if (ranges is null)
                reached[step.Id] = ranges = [];
            ranges.Add(step.Range);

            if (catalog.FindPlugin(step.Id) is not { } plugin)
                continue;
            foreach (var version in plugin.Versions.Where(version => version.PublishedAt is not null && step.Range.Contains(version.Version, allowPreRelease: true)))
            {
                foreach (var next in version.Dependencies)
                    queue.Enqueue((next.Id, next.Range, $"{step.Path} -> {next.Id}"));
            }
        }

        foreach (var cycle in cycles.Distinct(StringComparer.Ordinal))
            yield return $"The dependencies close a cycle: {cycle}.";
        if (reached.Count > MaxClosure)
            yield return string.Create(CultureInfo.InvariantCulture, $"The dependencies bring in {reached.Count} plugins; at most {MaxClosure} are allowed.");
    }

    private static IEnumerable<string> CheckAssemblyNames(PackagedManifest manifest, RegistryState registry)
    {
        var own = new[] { manifest.Assemblies.Contracts.File, manifest.Assemblies.Implementation.File };
        foreach (var registration in registry.Registrations.Where(other => !string.Equals(other.Id, manifest.Id, StringComparison.Ordinal)))
        {
            var taken = registry.VersionsOf(registration.Id).Select(record => record.Contracts.Assembly).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in own.Where(taken.Contains))
                yield return $"{name} has the name of the contracts assembly of {registration.Id}; rename the assembly.";
        }
    }

    private static bool MayDependOn(PluginTier dependent, PluginTier dependency) => dependent switch
    {
        PluginTier.Official => dependency == PluginTier.Official,
        PluginTier.Verified => dependency is PluginTier.Official or PluginTier.Verified,
        _ => true,
    };

    private static string Allowed(PluginTier tier) => tier == PluginTier.Official ? "official" : "official or verified";

    private static string Name(PluginTier tier) => tier.ToString().ToLowerInvariant();
}
