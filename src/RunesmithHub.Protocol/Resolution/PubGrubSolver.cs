namespace RunesmithHub.Protocol.Resolution;

/// <summary>The PubGrub version solving algorithm over finite version sets.</summary>
internal sealed class PubGrubSolver(IPackageSource source)
{
    private readonly Dictionary<string, List<Incompatibility>> _byPackage = new(StringComparer.Ordinal);
    private readonly HashSet<Incompatibility> _added = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _introduced = new(StringComparer.Ordinal);
    private readonly PartialSolution _solution = new();

    /// <summary>Solves, returning the chosen version index of every selected package except the root.</summary>
    /// <exception cref="SolveFailedException">No solution exists; the exception holds the root cause.</exception>
    public IReadOnlyDictionary<string, int> Solve()
    {
        var root = source.RootPackage;
        Add(new Incompatibility([new Term(root, VersionSet.Full(1), IsPositive: false)], new RootCause(), root));

        string? next = root;
        while (next is not null)
        {
            Propagate(next);
            next = DecideNext();
        }

        return _solution.Decisions.Where(pair => pair.Key != root).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private void Add(Incompatibility incompatibility)
    {
        if (!_added.Add(incompatibility))
            return;

        foreach (var term in incompatibility.Terms)
        {
            if (!_byPackage.TryGetValue(term.Package, out var list))
                _byPackage[term.Package] = list = [];
            list.Add(incompatibility);
        }

        foreach (var term in incompatibility.Terms)
        {
            if (_introduced.Add(term.Package))
            {
                foreach (var fact in source.Facts(term.Package))
                    Add(fact);
            }
        }
    }

    private void Propagate(string package)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal) { package };
        while (changed.Count > 0)
        {
            var current = changed.First();
            changed.Remove(current);
            var incompatibilities = _byPackage.GetValueOrDefault(current) ?? [];
            for (var i = incompatibilities.Count - 1; i >= 0; i--)
            {
                var incompatibility = incompatibilities[i];
                var result = PropagateOne(incompatibility, out var derived);
                if (result == Propagation.Conflict)
                {
                    var rootCause = ResolveConflict(incompatibility);
                    if (PropagateOne(rootCause, out derived) != Propagation.Derived)
                        throw new InvalidOperationException("The learned incompatibility did not derive a term.");
                    changed.Clear();
                    changed.Add(derived!);
                    break;
                }

                if (result == Propagation.Derived)
                    changed.Add(derived!);
            }
        }
    }

    private Propagation PropagateOne(Incompatibility incompatibility, out string? derived)
    {
        derived = null;
        Term? unsatisfied = null;
        foreach (var term in incompatibility.Terms)
        {
            if (_solution.Satisfies(term))
                continue;
            if (_solution.Contradicts(term))
                return Propagation.None;
            if (unsatisfied is not null)
                return Propagation.None;
            unsatisfied = term;
        }

        if (unsatisfied is null)
            return Propagation.Conflict;

        _solution.Derive(unsatisfied.Negate(), incompatibility);
        derived = unsatisfied.Package;
        return Propagation.Derived;
    }

    private Incompatibility ResolveConflict(Incompatibility incompatibility)
    {
        var learned = false;
        while (!incompatibility.IsFailure)
        {
            Term? mostRecentTerm = null;
            Assignment? mostRecentSatisfier = null;
            Term? difference = null;
            var previousSatisfierLevel = 1;

            foreach (var term in incompatibility.Terms)
            {
                var satisfier = _solution.Satisfier(term);
                if (mostRecentSatisfier is null || mostRecentSatisfier.Index < satisfier.Index)
                {
                    if (mostRecentSatisfier is not null)
                        previousSatisfierLevel = Math.Max(previousSatisfierLevel, mostRecentSatisfier.DecisionLevel);
                    mostRecentTerm = term;
                    mostRecentSatisfier = satisfier;
                    difference = null;
                }
                else
                {
                    previousSatisfierLevel = Math.Max(previousSatisfierLevel, satisfier.DecisionLevel);
                }

                if (ReferenceEquals(mostRecentTerm, term))
                {
                    var rest = mostRecentSatisfier.Term.Difference(mostRecentTerm);
                    difference = rest.IsPositive && rest.Set.IsEmpty ? null : rest;
                    if (difference is not null)
                        previousSatisfierLevel = Math.Max(previousSatisfierLevel, _solution.Satisfier(difference.Negate()).DecisionLevel);
                }
            }

            if (mostRecentSatisfier!.IsDecision || previousSatisfierLevel < mostRecentSatisfier.DecisionLevel)
            {
                _solution.Backtrack(previousSatisfierLevel);
                if (learned)
                    Add(incompatibility);
                return incompatibility;
            }

            var cause = mostRecentSatisfier.Cause!;
            var terms = incompatibility.Terms.Where(term => !ReferenceEquals(term, mostRecentTerm))
                .Concat(cause.Terms.Where(term => term.Package != mostRecentSatisfier.Term.Package))
                .ToList();
            if (difference is not null)
                terms.Add(difference.Negate());

            incompatibility = new Incompatibility(terms, new ConflictCause(incompatibility, cause), source.RootPackage);
            learned = true;
        }

        throw new SolveFailedException(incompatibility);
    }

    private string? DecideNext()
    {
        var candidates = _solution.Undecided().ToList();
        if (candidates.Count == 0)
            return null;

        var (package, term) = candidates
            .OrderBy(candidate => source.Priority(candidate.Package))
            .ThenBy(candidate => candidate.Term.Set.Count)
            .ThenBy(candidate => candidate.Package, StringComparer.Ordinal)
            .First();

        if (term.Set.IsEmpty)
        {
            Add(new Incompatibility([term], new NoVersionsCause(package), source.RootPackage));
            return package;
        }

        var version = source.Choose(package, term.Set);
        var conflict = false;
        foreach (var dependency in source.Dependencies(package, version))
        {
            Add(dependency);
            conflict |= dependency.Terms.All(t => t.Package == package || _solution.Satisfies(t));
        }

        if (!conflict)
            _solution.Decide(package, version, source.VersionCount(package));
        return package;
    }

    private enum Propagation
    {
        None,
        Derived,
        Conflict,
    }
}
