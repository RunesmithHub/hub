using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Protocol.Resolution;

/// <summary>
/// Decides which plugin versions to install, update, keep or remove for a request, with PubGrub over the versions in the index.
/// </summary>
/// <remarks>
/// A plan has one version of each plugin; satisfies every dependency range, the trust rules, the API and platform of the host and the
/// tier setting; never chooses a blocked version; chooses frozen, yanked, deleted and held versions only when exactly that version is
/// installed; and chooses pre-releases only when allowed. Among valid plans it changes as little as possible, keeps installed versions,
/// and otherwise prefers the highest versions.
/// </remarks>
public sealed class Resolver(HubCatalog catalog, ResolutionSettings settings)
{
    /// <summary>Resolves a request against what is installed.</summary>
    /// <param name="installed">The installed hub plugins; bundled and local plugins are not included.</param>
    public ResolutionResult Resolve(IReadOnlyList<InstalledPlugin> installed, ResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(request);
        var byId = installed.ToDictionary(plugin => plugin.Id, StringComparer.Ordinal);

        if (MissingTarget(request) is { } missing)
            return new ResolutionResult { Failure = missing };

        var removed = request.RemovedIds.Where(byId.ContainsKey).ToHashSet(StringComparer.Ordinal);
        if (NeededByOthers(byId, removed) is { } needed)
            return new ResolutionResult { Failure = needed };

        var problem = new ResolutionProblem(catalog, settings, byId, request);

        IReadOnlyDictionary<string, int> solution;
        try
        {
            solution = new PubGrubSolver(problem).Solve();
        }
        catch (SolveFailedException exception)
        {
            return new ResolutionResult { Failure = new FailureExplainer(problem).Explain(exception.Incompatibility) };
        }

        return new ResolutionResult { Plan = BuildPlan(problem, byId, removed, solution) };
    }

    private ResolutionFailure? MissingTarget(ResolutionRequest request)
    {
        if (request.Kind is not (RequestKind.Install or RequestKind.Update) || request.PluginId is not { } id)
            return null;
        if (catalog.FindPlugin(id) is not { } record)
            return NotFound($"{id} is not in the hub.");
        if (request.Version is { } version && record.FindVersion(version) is null)
            return NotFound($"{record.Name} has no version {version}.");
        return null;
    }

    private static ResolutionFailure NotFound(string message) => new(ResolutionFailureKind.NotFound, message, [message], []);

    private ResolutionFailure? NeededByOthers(Dictionary<string, InstalledPlugin> installed, HashSet<string> removed)
    {
        if (removed.Count == 0)
            return null;

        var needing = new List<string>();
        var queue = new Queue<string>(removed);
        while (queue.Count > 0)
        {
            var needed = queue.Dequeue();
            foreach (var plugin in installed.Values.Where(plugin => !removed.Contains(plugin.Id) && !needing.Contains(plugin.Id)))
            {
                var record = catalog.FindPlugin(plugin.Id)?.FindVersion(plugin.Version);
                if (record is not null && record.Dependencies.Any(dependency => dependency.Id == needed))
                {
                    needing.Add(plugin.Id);
                    queue.Enqueue(plugin.Id);
                }
            }
        }

        if (needing.Count == 0)
            return null;

        var names = PlainText.List(needing.Select(Name));
        var message = $"{names} {(needing.Count == 1 ? "needs" : "need")} {PlainText.List(removed.Order(StringComparer.Ordinal).Select(Name))}. " +
            $"Remove {(needing.Count == 1 ? "it" : "them")} too, or cancel.";
        return new ResolutionFailure(ResolutionFailureKind.NeededByOthers, message, [message], needing);
    }

    private InstallPlan BuildPlan(ResolutionProblem problem, Dictionary<string, InstalledPlugin> installed, HashSet<string> removed, IReadOnlyDictionary<string, int> solution)
    {
        var removals = new List<PlanOperation>();
        foreach (var plugin in installed.Values.Where(plugin => !solution.ContainsKey(plugin.Id)).OrderBy(plugin => plugin.Id, StringComparer.Ordinal))
        {
            var package = problem.Package(plugin.Id);
            if (package.Record is null)
            {
                removals.Add(new PlanOperation(OperationKind.Keep, plugin.Id, plugin.Id, plugin.Version, plugin.Version, null, plugin.Requested ? OperationReason.Requested : OperationReason.Dependency, null));
                continue;
            }

            var reason = removed.Contains(plugin.Id) ? OperationReason.Requested
                : IsUnavailable(problem, package, plugin) ? OperationReason.Unavailable
                : OperationReason.NoLongerNeeded;
            removals.Add(new PlanOperation(OperationKind.Remove, plugin.Id, package.Record.Name, plugin.Version, null, package.Record.Tier, reason, null));
        }

        var changes = new List<PlanOperation>();
        foreach (var id in DependencyOrder(problem, solution))
        {
            var package = problem.Package(id);
            var record = package.Versions[solution[id]];
            var from = installed.GetValueOrDefault(id)?.Version;
            var kind = from is null ? OperationKind.Install
                : from == record.Version ? OperationKind.Keep
                : record.Version > from ? OperationKind.Update
                : OperationKind.Downgrade;
            var reason = problem.Roots.Contains(id) ? OperationReason.Requested : OperationReason.Dependency;
            changes.Add(new PlanOperation(kind, id, package.Record!.Name, from, record.Version, package.Record.Tier, reason, record));
        }

        return new InstallPlan([.. removals.Where(op => op.Kind == OperationKind.Remove), .. changes, .. removals.Where(op => op.Kind == OperationKind.Keep)]);
    }

    private bool IsUnavailable(ResolutionProblem problem, PackageVersions package, InstalledPlugin plugin)
    {
        var version = package.Record!.FindVersion(plugin.Version);
        return (version is not null && catalog.GetEffectiveState(package.Record, version).State == VersionState.Blocked)
            || (plugin.Requested && !problem.HasAnyCandidate(plugin.Id));
    }

    /// <summary>Orders the selected plugins so dependencies come before dependents; a cycle is broken at the plugin first reached.</summary>
    private static List<string> DependencyOrder(ResolutionProblem problem, IReadOnlyDictionary<string, int> solution)
    {
        var ordered = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string id)
        {
            if (!visited.Add(id))
                return;
            var record = problem.Package(id).Versions[solution[id]];
            foreach (var dependency in record.Dependencies.Select(d => d.Id).Where(solution.ContainsKey).Order(StringComparer.Ordinal))
                Visit(dependency);
            ordered.Add(id);
        }

        foreach (var id in solution.Keys.Order(StringComparer.Ordinal))
            Visit(id);
        return ordered;
    }

    private string Name(string id) => catalog.FindPlugin(id)?.Name ?? id;
}
