using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Protocol.Resolution;

/// <summary>
/// Explains why no plan exists. Recognises the common situations (a tier the settings exclude, the trust rules, an incompatible Runesmith,
/// unavailable versions, two plugins needing different lines) and says them in a sentence or two with a fix when one exists; anything
/// else gets the full derivation.
/// </summary>
internal sealed class FailureExplainer(ResolutionProblem problem)
{
    private readonly DerivationWriter _writer = new(problem);

    public ResolutionFailure Explain(Incompatibility failure)
    {
        var derivation = _writer.Write(failure);
        var leaves = Leaves(failure);
        var message = TierSetting(leaves) ?? Trust(leaves) ?? Api(leaves) ?? DisjointRanges(leaves) ?? UnavailableVersions(leaves) ?? string.Join(" ", derivation);
        return new ResolutionFailure(ResolutionFailureKind.NoSolution, message, derivation, []);
    }

    private static List<Incompatibility> Leaves(Incompatibility failure)
    {
        var leaves = new List<Incompatibility>();
        var seen = new HashSet<Incompatibility>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Incompatibility>([failure]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
                continue;
            if (current.Cause is ConflictCause conflict)
            {
                pending.Push(conflict.Right);
                pending.Push(conflict.Left);
            }
            else
            {
                leaves.Add(current);
            }
        }

        return leaves;
    }

    private string? TierSetting(List<Incompatibility> leaves)
    {
        var excluded = leaves.Select(leaf => leaf.Cause).OfType<UnavailableCause>().Where(cause => cause.Reason == Unavailability.TierSetting).Select(cause => cause.Package)
            .Concat(leaves.Select(leaf => leaf.Cause).OfType<TrustCause>().Select(cause => cause.Dependency))
            .FirstOrDefault(id => problem.Package(id).Record is { } plugin && !problem.Settings.AllowedTiers.Allows(plugin.Tier));
        if (excluded is null)
            return null;

        var record = problem.Package(excluded).Record!;
        var settings = $"Your settings allow only {PlainText.Allowed(problem.Settings.AllowedTiers)} plugins.";
        var dependents = Dependents(leaves, excluded);
        if (dependents.Count == 0 || problem.Roots.Contains(excluded))
            return $"{record.Name} is {PlainText.Tier(record.Tier)}. {settings}";
        return $"{PlainText.List(dependents.Select(problem.NameOf))} {Needs(dependents)} {record.Name}, which is {PlainText.Tier(record.Tier)}. {settings}";
    }

    private string? Trust(List<Incompatibility> leaves)
    {
        if (leaves.Select(leaf => leaf.Cause).OfType<TrustCause>().FirstOrDefault() is not { } trust)
            return null;

        var dependent = problem.Package(trust.Dependent).Record!;
        var target = problem.Package(trust.Dependency).Record!;
        var dependentTier = PlainText.Capitalize(PlainText.Tier(dependent.Tier));
        return trust.Problem == TrustProblem.Tier
            ? $"{dependent.Name} needs {target.Name}, which is {PlainText.Tier(target.Tier)}. {dependentTier} plugins can only depend on {(dependent.Tier == PluginTier.Official ? "official" : "official and verified")} plugins."
            : $"{dependent.Name} needs a reviewed version of {target.Name}, and none of the versions it accepts has been reviewed. {dependentTier} plugins can only use reviewed versions.";
    }

    private string? Api(List<Incompatibility> leaves)
    {
        foreach (var cause in leaves.Select(leaf => leaf.Cause).OfType<UnavailableCause>().Where(cause => cause.Reason == Unavailability.Api))
        {
            var package = problem.Package(cause.Package);
            var runnable = Enumerable.Range(0, package.Versions.Count).Where(index => problem.Unavailable(package, index) is not Unavailability.Api and not Unavailability.Blocked);
            if (runnable.Any())
                continue;

            var host = problem.Settings.Host;
            var newest = package.Versions[^1];
            var needed = newest.RunesmithApi.Minimum.Line;
            var provided = host.ApiVersion.Line;
            var fix = newest.RunesmithApi.Minimum > host.ApiVersion ? "Update Runesmith." : $"{package.Record!.Name} needs a new version for this Runesmith.";
            var dependents = Dependents(leaves, cause.Package);
            var lead = dependents.Count == 0 || problem.Roots.Contains(cause.Package)
                ? $"No version of {package.Record!.Name} runs on this version of Runesmith."
                : $"{PlainText.List(dependents.Select(problem.NameOf))} {Needs(dependents)} {package.Record!.Name}, but no version of it runs on this version of Runesmith.";
            return $"{lead} The newest version needs Runesmith API {needed}; this Runesmith provides {provided}. {fix}";
        }

        return null;
    }

    private string? UnavailableVersions(List<Incompatibility> leaves)
    {
        foreach (var leaf in leaves)
        {
            if (leaf.Cause is DependencyCause dependency && leaf.TermFor(dependency.Dependency) is not null)
            {
                var target = problem.Package(dependency.Dependency);
                var inRange = problem.RangeSet(target, dependency.Range);
                if (AllUnavailable(target, inRange) is not { } reason)
                    continue;

                var dependent = problem.NameOf(dependency.Dependent);
                var range = PlainText.Range(dependency.Range);
                var which = inRange.Count == 1
                    ? $"{target.Record!.Name} {target.Versions[inRange.Indexes().Single()].Version}, the only matching version,"
                    : $"every {range} version of {target.Record!.Name}";
                var lead = $"{dependent} needs {target.Record.Name} {range}, and {which} is {_writer.ReasonText(reason, target.Record.Tier)}.";
                var outside = Enumerable.Range(0, target.Versions.Count).Where(index => !inRange.Contains(index) && problem.Unavailable(target, index) is null).ToList();
                var verb = problem.IsInstalled(dependency.Dependent) ? "be used" : "be installed";
                var fix = outside.Count > 0
                    ? $"{dependent} can't {verb} until it moves to {target.Record.Name} {target.Versions[outside[^1]].Version.Line}."
                    : $"{dependent} can't {verb} until {target.Record.Name} has a version it can use.";
                return $"{lead} {fix}";
            }

            if (leaf.Cause is RequirementCause requirement && leaf.TermFor(requirement.Package) is { } term)
            {
                var package = problem.Package(requirement.Package);
                if (package.Record is null || AllUnavailable(package, term.Set) is not { } reason)
                    continue;
                var subject = term.Set.Count == 1 ? $"{package.Record.Name} {package.Versions[term.Set.Indexes().Single()].Version} is" : $"Every version of {package.Record.Name} is";
                return $"{subject} {_writer.ReasonText(reason, package.Record.Tier)}.";
            }
        }

        return null;
    }

    /// <summary>Returns the reason every version in the set can't be chosen, when they all can't and share the most common reason.</summary>
    private Unavailability? AllUnavailable(PackageVersions package, VersionSet set)
    {
        if (set.IsEmpty)
            return null;
        var reasons = set.Indexes().Select(index => problem.Unavailable(package, index)).ToList();
        if (reasons.Any(reason => reason is null))
            return null;
        return reasons.GroupBy(reason => reason!.Value).OrderByDescending(group => group.Count()).First().Key;
    }

    private string? DisjointRanges(List<Incompatibility> leaves)
    {
        var dependencies = leaves
            .Select(leaf => (Leaf: leaf, Cause: leaf.Cause as DependencyCause))
            .Where(pair => pair.Cause is not null && pair.Leaf.TermFor(pair.Cause.Dependency) is not null)
            .GroupBy(pair => pair.Cause!.Dependency, StringComparer.Ordinal);

        foreach (var group in dependencies)
        {
            var edges = group.GroupBy(pair => pair.Cause!.Dependent, StringComparer.Ordinal)
                .Select(byDependent => byDependent.First())
                .OrderBy(edge => problem.IsInstalled(edge.Cause!.Dependent) ? 1 : 0)
                .ToList();
            if (edges.Count < 2)
                continue;

            var target = problem.Package(group.Key);
            var available = VersionSet.Of(target.Versions.Count, index => problem.Unavailable(target, index) is null);
            var first = edges[0];
            var second = edges[1];
            var firstSet = problem.RangeSet(target, first.Cause!.Range).Intersect(available);
            var secondSet = problem.RangeSet(target, second.Cause!.Range).Intersect(available);
            if (!firstSet.IsDisjointFrom(secondSet))
                continue;

            var name = target.Record!.Name;
            var needs = edges.Take(2).Select(edge =>
                $"{_writer.TermText(edge.Leaf.TermFor(edge.Cause!.Dependent)!)} needs {name} {PlainText.Range(edge.Cause.Range)}");
            var lead = $"{PlainText.Capitalize(string.Join(", and ", needs))}. No version of {name} satisfies both.";
            var fixes = new[] { Fix(second.Cause, firstSet), Fix(first.Cause, secondSet) }.Where(fix => fix is not null).ToList();
            return fixes.Count == 0 ? lead : $"{lead} {PlainText.Capitalize(string.Join(", or ", fixes))}.";
        }

        return null;
    }

    /// <summary>Suggests a version of a plugin the user wants that works with the other side's versions, or removing it when it is installed.</summary>
    private string? Fix(DependencyCause edge, VersionSet otherSide)
    {
        if (!problem.Roots.Contains(edge.Dependent))
            return null;

        var dependent = problem.Package(edge.Dependent);
        var name = dependent.Record!.Name;
        var current = dependent.InstalledIndex;
        for (var index = dependent.Versions.Count - 1; index >= 0; index--)
        {
            if (index == current || problem.Unavailable(dependent, index) is not null)
                continue;
            var version = dependent.Versions[index];
            var dependency = version.Dependencies.FirstOrDefault(d => d.Id == edge.Dependency);
            if (dependency is not null
                && (dependency.Range.Equals(edge.Range) || problem.RangeSet(problem.Package(edge.Dependency), dependency.Range).IsDisjointFrom(otherSide)))
                continue;

            if (current is { } installed && index > installed)
                return $"update {name}";
            var works = dependency is null
                ? $"which doesn't need {problem.NameOf(edge.Dependency)}"
                : $"which works with {problem.NameOf(edge.Dependency)} {PlainText.Range(dependency.Range)}";
            return $"install {name} {version.Version}, {works}";
        }

        return problem.IsInstalled(edge.Dependent) ? $"remove {name}" : null;
    }

    private static List<string> Dependents(List<Incompatibility> leaves, string package) =>
        [.. leaves.Select(leaf => leaf.Cause switch
            {
                DependencyCause dependency when dependency.Dependency == package => dependency.Dependent,
                TrustCause trust when trust.Dependency == package => trust.Dependent,
                _ => null,
            })
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)];

    private static string Needs(List<string> dependents) => dependents.Count == 1 ? "needs" : "need";
}
