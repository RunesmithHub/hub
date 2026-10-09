namespace RunesmithHub.Protocol.Resolution;

/// <summary>The decisions and derivations made so far, in order, with each package's combined term.</summary>
internal sealed class PartialSolution
{
    private readonly List<Assignment> _assignments = [];
    private readonly Dictionary<string, Term> _terms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _decisions = new(StringComparer.Ordinal);

    /// <summary>Gets the number of decisions made; derivations before the first decision are at level 0.</summary>
    public int DecisionLevel => _decisions.Count;

    /// <summary>Gets the chosen version index of every decided package.</summary>
    public IReadOnlyDictionary<string, int> Decisions => _decisions;

    public void Decide(string package, int version, int size)
    {
        _decisions[package] = version;
        Add(new Assignment(new Term(package, VersionSet.Single(size, version), IsPositive: true), _decisions.Count, _assignments.Count, null));
    }

    public void Derive(Term term, Incompatibility cause) => Add(new Assignment(term, DecisionLevel, _assignments.Count, cause));

    private void Add(Assignment assignment)
    {
        _assignments.Add(assignment);
        var package = assignment.Term.Package;
        _terms[package] = _terms.TryGetValue(package, out var existing) ? existing.Intersect(assignment.Term) : assignment.Term;
    }

    public Term? TermFor(string package) => _terms.GetValueOrDefault(package);

    public bool Satisfies(Term term) => TermFor(term.Package) is { } known && known.Satisfies(term);

    public bool Contradicts(Term term) => TermFor(term.Package) is { } known && known.Contradicts(term);

    /// <summary>Gets the packages that must be selected but have no decision yet.</summary>
    public IEnumerable<(string Package, Term Term)> Undecided() =>
        _terms.Where(pair => pair.Value.IsPositive && !_decisions.ContainsKey(pair.Key)).Select(pair => (pair.Key, pair.Value));

    /// <summary>Finds the earliest assignment after which the partial solution satisfies the term.</summary>
    public Assignment Satisfier(Term term)
    {
        Term? accumulated = null;
        foreach (var assignment in _assignments)
        {
            if (assignment.Term.Package != term.Package)
                continue;
            accumulated = accumulated is null ? assignment.Term : accumulated.Intersect(assignment.Term);
            if (accumulated.Satisfies(term))
                return assignment;
        }

        throw new InvalidOperationException($"Nothing satisfies the term for {term.Package}.");
    }

    /// <summary>Removes every assignment made after the given decision level.</summary>
    public void Backtrack(int level)
    {
        _assignments.RemoveAll(assignment => assignment.DecisionLevel > level);
        _terms.Clear();
        _decisions.Clear();
        var kept = _assignments.ToList();
        _assignments.Clear();
        foreach (var assignment in kept)
        {
            if (assignment.IsDecision)
                _decisions[assignment.Term.Package] = assignment.Term.Set.Indexes().Single();
            Add(assignment with { Index = _assignments.Count });
        }
    }
}
