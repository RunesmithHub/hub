namespace RunesmithHub.Protocol.Resolution;

/// <summary>A set of terms that can't all hold at once.</summary>
internal sealed class Incompatibility
{
    public Incompatibility(IEnumerable<Term> terms, IncompatibilityCause cause, string rootPackage)
    {
        var merged = new Dictionary<string, Term>(StringComparer.Ordinal);
        foreach (var term in terms)
            merged[term.Package] = merged.TryGetValue(term.Package, out var existing) ? existing.Intersect(term) : term;

        var list = merged.Values.ToList();
        if (cause is ConflictCause && list.Count > 1)
            list.RemoveAll(term => term.IsPositive && term.Package == rootPackage);

        Terms = list;
        Cause = cause;
        RootPackage = rootPackage;
    }

    public IReadOnlyList<Term> Terms { get; }

    public IncompatibilityCause Cause { get; }

    public string RootPackage { get; }

    /// <summary>Gets whether this incompatibility says the resolution as a whole can't succeed.</summary>
    public bool IsFailure => Terms.Count == 0 || (Terms.Count == 1 && Terms[0].IsPositive && Terms[0].Package == RootPackage);

    public Term? TermFor(string package) => Terms.FirstOrDefault(term => term.Package == package);
}
