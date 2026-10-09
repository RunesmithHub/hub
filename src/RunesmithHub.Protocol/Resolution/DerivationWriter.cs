namespace RunesmithHub.Protocol.Resolution;

/// <summary>Writes the solver's derivation of a failure as sentences, each fact before the conclusions drawn from it.</summary>
internal sealed class DerivationWriter(ResolutionProblem problem)
{
    public IReadOnlyList<string> Write(Incompatibility failure)
    {
        var lines = new List<string>();
        Visit(failure, lines, new HashSet<Incompatibility>(ReferenceEqualityComparer.Instance));
        if (lines.Count == 0)
            lines.Add(PlainText.Capitalize(Describe(failure)) + ".");
        return lines;
    }

    private void Visit(Incompatibility incompatibility, List<string> lines, HashSet<Incompatibility> done)
    {
        if (incompatibility.Cause is not ConflictCause conflict || !done.Add(incompatibility))
            return;

        Visit(conflict.Left, lines, done);
        Visit(conflict.Right, lines, done);
        lines.Add($"Because {Describe(conflict.Left)}, and {Describe(conflict.Right)}, {Describe(incompatibility)}.");
    }

    /// <summary>Describes what an incompatibility says, starting in lowercase unless it starts with a name.</summary>
    public string Describe(Incompatibility incompatibility) => incompatibility.Cause switch
    {
        RequirementCause requirement => Requirement(requirement),
        RemovalCause removal => $"you asked to remove {Name(removal.Package)}",
        DependencyCause dependency => Dependency(incompatibility, dependency),
        TrustCause trust => Trust(incompatibility, trust),
        UnavailableCause unavailable => Unavailable(incompatibility, unavailable),
        NoVersionsCause noVersions => $"no version of {Name(noVersions.Package)} is left to choose",
        _ => Derived(incompatibility),
    };

    private string Requirement(RequirementCause requirement) => requirement.Kind switch
    {
        RequirementKind.Install when requirement.Version is { } version => $"you asked to install {Name(requirement.Package)} {version}",
        RequirementKind.Install => $"you asked to install {Name(requirement.Package)}",
        RequirementKind.Update => $"you asked to update {Name(requirement.Package)}",
        _ => $"{Name(requirement.Package)} is installed",
    };

    private string Dependency(Incompatibility incompatibility, DependencyCause dependency)
    {
        var dependent = TermText(incompatibility.TermFor(dependency.Dependent)!);
        var target = problem.Package(dependency.Dependency);
        if (target.Record is null)
            return $"{dependent} needs {Name(dependency.Dependency)}, which is not in the hub";
        var range = PlainText.Range(dependency.Range);
        return incompatibility.TermFor(dependency.Dependency) is null
            ? $"{dependent} needs {Name(dependency.Dependency)} {range}, which no version matches"
            : $"{dependent} needs {Name(dependency.Dependency)} {range}";
    }

    private string Trust(Incompatibility incompatibility, TrustCause trust)
    {
        var dependent = problem.Package(trust.Dependent).Record!;
        var target = problem.Package(trust.Dependency).Record!;
        var dependentText = TermText(incompatibility.TermFor(trust.Dependent)!);
        if (trust.Problem == TrustProblem.Tier)
            return $"{dependentText} needs {target.Name}, which is {PlainText.Tier(target.Tier)}, and {PlainText.Tier(dependent.Tier)} plugins can't depend on {PlainText.Tier(target.Tier)} ones";

        var versions = TermText(incompatibility.TermFor(trust.Dependency)!);
        return $"{dependentText} can only use reviewed versions of {target.Name}, and {versions} {(IsPlural(incompatibility.TermFor(trust.Dependency)!) ? "are" : "is")} not reviewed";
    }

    private string Unavailable(Incompatibility incompatibility, UnavailableCause unavailable)
    {
        var term = incompatibility.TermFor(unavailable.Package)!;
        var record = problem.Package(unavailable.Package).Record!;
        var subject = term.Set.IsFull && term.Set.Count > 1 ? $"every version of {record.Name}" : TermText(term);
        var verb = term.Set.IsFull || !IsPlural(term) ? "is" : "are";
        return $"{subject} {verb} {ReasonText(unavailable.Reason, record.Tier)}";
    }

    public string ReasonText(Unavailability reason, Index.PluginTier tier) => reason switch
    {
        Unavailability.Blocked => "blocked",
        Unavailability.TierSetting => $"{PlainText.Tier(tier)}, and your settings allow only {PlainText.Allowed(problem.Settings.AllowedTiers)} plugins",
        Unavailability.Api => "not compatible with this version of Runesmith",
        Unavailability.Platform => "not available for this platform",
        Unavailability.Frozen => "frozen",
        Unavailability.Deleted => "deleted",
        Unavailability.Yanked => "withdrawn by its developer",
        Unavailability.InReview => "in review",
        _ => "not available yet",
    };

    private string Derived(Incompatibility incompatibility)
    {
        var terms = incompatibility.Terms.Where(term => term.Package != problem.RootPackage).ToList();
        if (incompatibility.IsFailure || terms.Count == 0)
            return "no install plan exists";

        return terms switch
        {
            [{ IsPositive: true } only] => $"{TermText(only)} can't be used",
            [{ IsPositive: false } only] when only.Set.IsFull => $"{Name(only.Package)} is required",
            [{ IsPositive: false } only] => $"{Name(only.Package)} must be {PlainText.Versions(problem.Package(only.Package), only.Set)}",
            [{ IsPositive: true } first, { IsPositive: true } second] => $"{TermText(first)} can't be used with {TermText(second)}",
            [{ IsPositive: true } first, { IsPositive: false } second] => $"{TermText(first)} needs {TermText(second)}",
            [{ IsPositive: false } first, { IsPositive: true } second] => $"{TermText(second)} needs {TermText(first)}",
            _ => PlainText.List(terms.Select(term => term.IsPositive ? TermText(term) : "not " + TermText(term))) + " can't all hold",
        };
    }

    /// <summary>Describes a term's package and versions, such as <c>Lumen Todo 1.0.0</c>, or just the name for every version.</summary>
    public string TermText(Term term)
    {
        var versions = PlainText.Versions(problem.Package(term.Package), term.Set);
        return versions.Length == 0 ? Name(term.Package) : $"{Name(term.Package)} {versions}";
    }

    private static bool IsPlural(Term term) => term.Set.Count > 1;

    private string Name(string id) => problem.NameOf(id);
}
