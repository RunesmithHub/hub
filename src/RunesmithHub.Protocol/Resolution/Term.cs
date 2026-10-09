namespace RunesmithHub.Protocol.Resolution;

/// <summary>
/// A statement about one package: positive means "selected, at a version in the set"; negative means "not selected, or at a version
/// outside the set".
/// </summary>
internal sealed record Term(string Package, VersionSet Set, bool IsPositive)
{
    public Term Negate() => this with { IsPositive = !IsPositive };

    /// <summary>Returns the term that holds exactly when both hold.</summary>
    public Term Intersect(Term other) => (IsPositive, other.IsPositive) switch
    {
        (true, true) => this with { Set = Set.Intersect(other.Set) },
        (true, false) => this with { Set = Set.Except(other.Set) },
        (false, true) => other with { Set = other.Set.Except(Set) },
        _ => this with { Set = Set.Union(other.Set) },
    };

    /// <summary>Returns the term that holds when this one does and the other does not.</summary>
    public Term Difference(Term other) => Intersect(other.Negate());

    /// <summary>Gets whether this term holding means the other holds.</summary>
    public bool Satisfies(Term other) => (IsPositive, other.IsPositive) switch
    {
        (true, true) => Set.IsSubsetOf(other.Set),
        (true, false) => Set.IsDisjointFrom(other.Set),
        (false, true) => false,
        _ => other.Set.IsSubsetOf(Set),
    };

    /// <summary>Gets whether this term holding means the other can't.</summary>
    public bool Contradicts(Term other) => (IsPositive, other.IsPositive) switch
    {
        (true, true) => Set.IsDisjointFrom(other.Set),
        (true, false) => Set.IsSubsetOf(other.Set),
        (false, true) => other.Set.IsSubsetOf(Set),
        _ => false,
    };
}
