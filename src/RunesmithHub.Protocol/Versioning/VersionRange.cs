using System.Diagnostics.CodeAnalysis;

namespace RunesmithHub.Protocol.Versioning;

/// <summary>
/// A version range such as <c>^1.4.2</c>, <c>~1.4.2</c>, <c>&gt;=1.4.0 &lt;1.9.0</c> or <c>^1.4.0 || ^2.1.0</c>.
/// </summary>
/// <remarks>
/// Comparators in a set are separated by single spaces and must all hold; sets are separated by <c> || </c> and any may hold.
/// <see cref="ToString"/> returns the canonical comparator form.
/// </remarks>
public sealed class VersionRange : IEquatable<VersionRange>
{
    private const string SetSeparator = " || ";
    private const int MaxLines = 100;

    private readonly VersionInterval[] _sets;
    private readonly string _text;

    private VersionRange(IEnumerable<VersionInterval> sets)
    {
        _sets = [.. sets
            .GroupBy(set => set.ToString(), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(set => set.Lower?.Version is null ? 0 : 1)
            .ThenBy(set => set.Lower?.Version)
            .ThenBy(set => set.Lower is { Inclusive: false } ? 1 : 0)
            .ThenBy(set => set.Upper is null ? 1 : 0)
            .ThenBy(set => set.Upper?.Version)
            .ThenBy(set => set.Upper is { Inclusive: true } ? 1 : 0)];
        _text = string.Join(SetSeparator, _sets.Select(set => set.ToString()));
    }

    /// <summary>Gets the comparator sets, sorted by their lower bounds.</summary>
    public IReadOnlyList<VersionInterval> Sets => _sets;

    /// <summary>Gets whether every set has an upper bound, as normalised ranges do.</summary>
    public bool IsBounded => _sets.All(set => set.Upper is not null);

    /// <summary>Gets the lowest version the range allows by its bounds: the lowest lower bound, or 0.0.0 when a set has none.</summary>
    public SemanticVersion Minimum => _sets.Select(set => set.Lower?.Version ?? new SemanticVersion(0, 0, 0)).Min()!;

    /// <summary>Gets whether every version the range allows is in one compatibility line.</summary>
    public bool StaysWithinOneLine => TryGetCoveredLines(out var lines) && lines.Count == 1;

    /// <summary>Gets whether the range allows versions from more than one compatibility line.</summary>
    public bool CrossesLines => !StaysWithinOneLine;

    /// <summary>Parses a range.</summary>
    /// <exception cref="FormatException">The text is not a valid range; the message says why.</exception>
    public static VersionRange Parse(string text) =>
        TryParse(text, out var range, out var error) ? range : throw new FormatException(error);

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out VersionRange? range) =>
        TryParse(text, out range, out _);

    /// <summary>Parses a range, giving the reason when it is invalid.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out VersionRange? range, [NotNullWhen(false)] out string? error)
    {
        range = null;
        if (string.IsNullOrEmpty(text))
        {
            error = "A version range can't be empty.";
            return false;
        }

        var sets = new List<VersionInterval>();
        foreach (var setText in text.Split(SetSeparator))
        {
            if (!TryParseSet(setText, out var set, out error))
                return false;
            sets.Add(set);
        }

        range = new VersionRange(sets);
        error = null;
        return true;
    }

    /// <summary>Creates the range that holds exactly one version.</summary>
    public static VersionRange Exactly(SemanticVersion version)
    {
        var bound = new VersionBound(version, Inclusive: true);
        return new VersionRange([new VersionInterval(bound, bound, [version])]);
    }

    private static bool TryParseSet(string text, [NotNullWhen(true)] out VersionInterval? set, [NotNullWhen(false)] out string? error)
    {
        set = null;
        if (text.Length == 0 || text.Contains("  ", StringComparison.Ordinal) || text[0] == ' ' || text[^1] == ' ')
        {
            error = $"\"{text}\" must be comparators separated by single spaces.";
            return false;
        }

        VersionBound? lower = null;
        VersionBound? upper = null;
        var named = new List<SemanticVersion>();
        foreach (var comparator in text.Split(' '))
        {
            if (!TryParseComparator(comparator, out var low, out var high, out var version, out error))
                return false;
            named.Add(version);
            lower = Tighter(lower, low, isLower: true);
            upper = Tighter(upper, high, isLower: false);
        }

        set = new VersionInterval(lower, upper, named);
        if (set.IsEmpty)
        {
            error = $"\"{text}\" matches no version.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseComparator(
        string text, out VersionBound? lower, out VersionBound? upper, [NotNullWhen(true)] out SemanticVersion? version, [NotNullWhen(false)] out string? error)
    {
        lower = null;
        upper = null;
        var (op, rest) = SplitOperator(text);
        var allowPartial = op is "^" or "~" or ">=";
        if (!SemanticVersion.TryParsePartial(rest, out version, out var wasPartial) || (wasPartial && !allowPartial))
        {
            error = rest.Length == 0
                ? $"\"{text}\" has no version."
                : $"\"{text}\" is not a valid comparator; use a full version such as 1.2.3, with ^, ~, >=, >, <=, < or =.";
            return false;
        }

        switch (op)
        {
            case "^":
                lower = new VersionBound(version, true);
                upper = new VersionBound(version.Line.NextStart, false);
                break;
            case "~":
                lower = new VersionBound(version, true);
                upper = new VersionBound(new SemanticVersion(version.Major, version.Minor + 1, 0), false);
                break;
            case ">=":
                lower = new VersionBound(version, true);
                break;
            case ">":
                lower = new VersionBound(version, false);
                break;
            case "<=":
                upper = new VersionBound(version, true);
                break;
            case "<":
                upper = new VersionBound(version, false);
                break;
            default:
                lower = new VersionBound(version, true);
                upper = new VersionBound(version, true);
                break;
        }

        error = null;
        return true;
    }

    private static (string Operator, string Version) SplitOperator(string text)
    {
        foreach (var op in (string[])[">=", "<=", "^", "~", ">", "<", "="])
        {
            if (text.StartsWith(op, StringComparison.Ordinal))
                return (op, text[op.Length..]);
        }

        return ("", text);
    }

    private static VersionBound? Tighter(VersionBound? current, VersionBound? candidate, bool isLower)
    {
        if (candidate is null)
            return current;
        if (current is null)
            return candidate;

        var order = candidate.Version.CompareTo(current.Version);
        if (order == 0)
            return candidate.Inclusive ? current : candidate;
        return (order > 0) == isLower ? candidate : current;
    }

    /// <summary>
    /// Returns the normalised range: every set without an upper bound gets <c>&lt;</c> the start of the line after its lower bound, so
    /// <c>&gt;=1.4.0</c> becomes <c>&gt;=1.4.0 &lt;2.0.0</c>.
    /// </summary>
    public VersionRange Normalize()
    {
        if (IsBounded)
            return this;

        return new VersionRange(_sets.Select(set => set.Upper is not null
            ? set
            : set.WithUpper(new VersionBound((set.Lower?.Version ?? new SemanticVersion(0, 0, 0)).Line.NextStart, Inclusive: false))));
    }

    /// <summary>
    /// Gets whether a version is in the range. A pre-release version only matches a set that names a pre-release of the same
    /// major.minor.patch, unless <paramref name="allowPreRelease"/> is set.
    /// </summary>
    public bool Contains(SemanticVersion version, bool allowPreRelease = false) => _sets.Any(set => set.Contains(version, allowPreRelease));

    /// <summary>Parses a range and gives its normalised canonical text, such as <c>&gt;=1.4.0 &lt;2.0.0</c> for <c>&gt;=1.4</c>.</summary>
    public static bool TryNormalize([NotNullWhen(true)] string? text, [NotNullWhen(true)] out string? normalized, [NotNullWhen(false)] out string? error)
    {
        normalized = TryParse(text, out var range, out error) ? range.Normalize().ToString() : null;
        return normalized is not null;
    }

    /// <summary>Lists the compatibility lines the range reaches, in order; false when it is unbounded or reaches more than 100 lines.</summary>
    public bool TryGetCoveredLines([NotNullWhen(true)] out IReadOnlyList<CompatibilityLine>? lines)
    {
        var found = new List<CompatibilityLine>();
        lines = null;
        foreach (var set in _sets)
        {
            if (!set.TryGetLines(MaxLines, found))
                return false;
        }

        found.Sort((left, right) => left.Start.CompareTo(right.Start));
        lines = found;
        return true;
    }

    public bool Equals(VersionRange? other) => other is not null && string.Equals(_text, other._text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is VersionRange other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_text);

    /// <summary>Returns the canonical text: sets sorted, each as its lower then upper comparator with full versions, joined with <c> || </c>.</summary>
    public override string ToString() => _text;
}
