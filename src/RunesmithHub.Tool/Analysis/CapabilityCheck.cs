using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Tool.Analysis.Detection;
using RunesmithHub.Tool.Analysis.Reporting;

namespace RunesmithHub.Tool.Analysis;

/// <summary>Compares detected capabilities with the declared ones, and both with the previous version's.</summary>
internal static class CapabilityCheck
{
    private const int SitesInMessage = 3;

    public static (CapabilityAnalysis Analysis, IReadOnlyList<AnalysisProblem> Problems) Check(
        PackagedManifest manifest, DetectedCapabilities detected, PackagedManifest? previousManifest, DetectedCapabilities? previousDetected)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(detected);
        var declared = manifest.Capabilities.Select(capability => capability.Id).ToHashSet(StringComparer.Ordinal);
        var problems = new List<AnalysisProblem>();
        foreach (var id in detected.Ids.Where(id => !declared.Contains(id)))
        {
            var sites = detected.SitesOf(id);
            var listed = string.Join("; ", sites.Take(SitesInMessage));
            var more = sites.Count > SitesInMessage ? $" and {sites.Count - SitesInMessage} more" : "";
            problems.Add(AnalysisProblem.Error(ProblemCodes.CapabilityUndeclared, $"The plugin uses the {id} capability without declaring it: {listed}{more}.", sites[0].Member));
        }

        foreach (var id in Protocol.Capabilities.All.Where(id => declared.Contains(id) && !detected.Contains(id)))
            problems.Add(AnalysisProblem.Warning(ProblemCodes.CapabilityUnused, $"The plugin declares {id}, but the analysis found no use of it; it may only be reached through the SDK."));

        var now = Union(declared, detected);
        var before = previousManifest is null ? [] : Union(previousManifest.Capabilities.Select(capability => capability.Id), previousDetected!);
        var detectedList = detected.Ids.Select(id =>
        {
            var sites = detected.SitesOf(id);
            return new DetectedCapability(id, [.. sites.Take(CapabilityAnalysis.MaxCallSites)], sites.Count);
        }).ToList();
        var analysis = new CapabilityAnalysis(
            manifest.Capabilities, detectedList, [.. Ordered(now.Except(before))], [.. Ordered(before.Except(now))]);
        return (analysis, problems);
    }

    /// <summary>Gets every capability a version declares or uses.</summary>
    public static HashSet<string> Union(IEnumerable<string> declared, DetectedCapabilities detected) =>
        [.. declared.Concat(detected.Ids)];

    private static IEnumerable<string> Ordered(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet(StringComparer.Ordinal);
        return Protocol.Capabilities.All.Where(set.Contains);
    }
}
