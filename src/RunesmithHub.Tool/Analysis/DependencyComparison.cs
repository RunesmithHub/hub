using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Tool.Analysis.Reporting;

namespace RunesmithHub.Tool.Analysis;

/// <summary>Lists a version's plugin dependencies and NuGet packages, and what changed since the previous version.</summary>
internal static class DependencyComparison
{
    /// <param name="components">The NuGet packages of the current version.</param>
    public static DependencyAnalysis Compare(PackagedManifest manifest, IReadOnlyList<SbomComponent> components, PackageSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(components);
        var packages = components.Select(component => new ReportedPackage(component.Id, component.Version, component.Bundled, component.License)).ToList();
        var changes = new List<DependencyChange>();
        if (previous is not null)
        {
            changes.AddRange(PluginChanges(previous.Manifest.Dependencies, manifest.Dependencies));
            changes.AddRange(PackageChanges(previous.Components, components));
        }

        return new DependencyAnalysis(manifest.Dependencies, packages, changes);
    }

    private static IEnumerable<DependencyChange> PluginChanges(IReadOnlyList<PluginDependency> before, IReadOnlyList<PluginDependency> after)
    {
        var old = before.ToDictionary(dependency => dependency.Id, StringComparer.Ordinal);
        var now = after.ToDictionary(dependency => dependency.Id, StringComparer.Ordinal);
        foreach (var id in old.Keys.Union(now.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var from = old.GetValueOrDefault(id)?.Range;
            var to = now.GetValueOrDefault(id)?.Range;
            if (from is null)
                yield return new DependencyChange("plugin", id, "added", null, to!.ToString());
            else if (to is null)
                yield return new DependencyChange("plugin", id, "removed", from.ToString(), null);
            else if (from.Minimum.Line != to.Minimum.Line)
                yield return new DependencyChange("plugin", id, "changed", from.ToString(), to.ToString());
        }
    }

    private static IEnumerable<DependencyChange> PackageChanges(IReadOnlyList<SbomComponent> before, IReadOnlyList<SbomComponent> after)
    {
        var old = before.GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => string.Join(", ", group.Select(package => package.Version)), StringComparer.OrdinalIgnoreCase);
        var now = after.GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => string.Join(", ", group.Select(package => package.Version)), StringComparer.OrdinalIgnoreCase);
        foreach (var id in old.Keys.Union(now.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var from = old.GetValueOrDefault(id);
            var to = now.GetValueOrDefault(id);
            if (from is null)
                yield return new DependencyChange("package", id, "added", null, to);
            else if (to is null)
                yield return new DependencyChange("package", id, "removed", from, null);
            else if (!string.Equals(from, to, StringComparison.Ordinal))
                yield return new DependencyChange("package", id, "changed", from, to);
        }
    }
}
