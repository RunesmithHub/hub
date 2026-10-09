namespace RunesmithHub.Protocol.Versioning;

/// <summary>One end of a <see cref="VersionInterval"/>.</summary>
/// <param name="Inclusive">Whether the bound's own version is inside the interval.</param>
public sealed record VersionBound(SemanticVersion Version, bool Inclusive);

/// <summary>A comparator set of a <see cref="VersionRange"/>: the versions between an optional lower and an optional upper bound.</summary>
public sealed class VersionInterval
{
    private readonly SemanticVersion[] _namedPreReleases;

    internal VersionInterval(VersionBound? lower, VersionBound? upper, IEnumerable<SemanticVersion> namedPreReleases)
    {
        Lower = lower;
        Upper = upper;
        _namedPreReleases = [.. namedPreReleases.Where(v => v.IsPreRelease)];
    }

    public VersionBound? Lower { get; }

    public VersionBound? Upper { get; }

    /// <summary>Gets whether no version at all lies between the bounds.</summary>
    public bool IsEmpty
    {
        get
        {
            if (Lower is null || Upper is null)
                return false;
            var order = Lower.Version.CompareTo(Upper.Version);
            return order > 0 || (order == 0 && !(Lower.Inclusive && Upper.Inclusive));
        }
    }

    /// <summary>Gets whether the interval holds exactly one version.</summary>
    public bool IsExact => Lower is { Inclusive: true } && Upper is { Inclusive: true } && Lower.Version == Upper.Version;

    /// <summary>Gets whether a version lies inside the interval, applying the pre-release rule unless <paramref name="allowPreRelease"/> is set.</summary>
    public bool Contains(SemanticVersion version, bool allowPreRelease = false)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!IsWithinBounds(version))
            return false;
        return !version.IsPreRelease || allowPreRelease || _namedPreReleases.Any(named => named.HasSameNumbers(version));
    }

    internal bool IsWithinBounds(SemanticVersion version)
    {
        if (Lower is not null)
        {
            var order = version.CompareTo(Lower.Version);
            if (order < 0 || (order == 0 && !Lower.Inclusive))
                return false;
        }

        if (Upper is not null)
        {
            var order = version.CompareTo(Upper.Version);
            if (order > 0 || (order == 0 && !Upper.Inclusive))
                return false;
        }

        return true;
    }

    internal VersionInterval WithUpper(VersionBound upper) => new(Lower, upper, _namedPreReleases);

    /// <summary>Lists the compatibility lines the interval reaches, or returns false when it is unbounded or reaches too many.</summary>
    internal bool TryGetLines(int limit, List<CompatibilityLine> lines)
    {
        if (Upper is null)
            return false;

        var line = CompatibilityLine.Of(Lower?.Version ?? new SemanticVersion(0, 0, 0));
        while (true)
        {
            if (!lines.Contains(line))
                lines.Add(line);
            if (lines.Count > limit)
                return false;

            var next = line.Next;
            var order = next.Start.CompareTo(Upper.Version);
            if (order > 0 || (order == 0 && !Upper.Inclusive))
                return true;
            line = next;
        }
    }

    /// <summary>Returns the canonical text: <c>1.2.3</c> for one version, otherwise the lower then the upper comparator.</summary>
    public override string ToString()
    {
        if (IsExact)
            return Lower!.Version.ToString();

        var parts = new List<string>(2);
        if (Lower is not null)
            parts.Add((Lower.Inclusive ? ">=" : ">") + Lower.Version);
        if (Upper is not null)
            parts.Add((Upper.Inclusive ? "<=" : "<") + Upper.Version);
        return string.Join(' ', parts);
    }
}
