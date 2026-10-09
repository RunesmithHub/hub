using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RunesmithHub.Protocol.Versioning;

/// <summary>A Semantic Versioning 2.0.0 version without build metadata, such as <c>1.4.2</c> or <c>2.0.0-beta.1</c>.</summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private readonly string[] _preRelease;

    /// <summary>Creates a version from its parts.</summary>
    /// <param name="preRelease">The dot-separated pre-release identifiers, or empty for a release.</param>
    /// <exception cref="ArgumentException">A part is negative or a pre-release identifier is malformed.</exception>
    public SemanticVersion(int major, int minor, int patch, params IEnumerable<string> preRelease)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);
        _preRelease = [.. preRelease];
        foreach (var identifier in _preRelease)
        {
            if (!IsValidPreReleaseIdentifier(identifier))
                throw new ArgumentException($"\"{identifier}\" is not a valid pre-release identifier.", nameof(preRelease));
        }

        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>Gets the pre-release identifiers; empty for a release.</summary>
    public IReadOnlyList<string> PreRelease => _preRelease;

    public bool IsPreRelease => _preRelease.Length > 0;

    /// <summary>Gets the compatibility line this version belongs to.</summary>
    public CompatibilityLine Line => CompatibilityLine.Of(this);

    /// <summary>Gets this version without its pre-release part.</summary>
    public SemanticVersion WithoutPreRelease => IsPreRelease ? new SemanticVersion(Major, Minor, Patch) : this;

    /// <summary>Parses a full version such as <c>1.4.2</c> or <c>1.5.0-beta.2</c>.</summary>
    /// <exception cref="FormatException">The text is not a valid version.</exception>
    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"\"{text}\" is not a valid semantic version.");

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        if (text is null || !TryParseCore(text, allowPartial: false, out version, out _))
            return false;
        return true;
    }

    /// <summary>Parses a version that may omit its minor and patch numbers, filling them in with zeros.</summary>
    internal static bool TryParsePartial(string text, [NotNullWhen(true)] out SemanticVersion? version, out bool wasPartial) =>
        TryParseCore(text, allowPartial: true, out version, out wasPartial);

    private static bool TryParseCore(string text, bool allowPartial, [NotNullWhen(true)] out SemanticVersion? version, out bool wasPartial)
    {
        version = null;
        wasPartial = false;
        if (text.Contains('+', StringComparison.Ordinal))
            return false;

        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var core = dash < 0 ? text : text[..dash];
        string[] preRelease = [];
        if (dash >= 0)
        {
            preRelease = text[(dash + 1)..].Split('.');
            if (!preRelease.All(IsValidPreReleaseIdentifier))
                return false;
        }

        var numbers = core.Split('.');
        if (numbers.Length is < 1 or > 3 || (numbers.Length < 3 && (!allowPartial || preRelease.Length > 0)))
            return false;

        var parts = new int[3];
        for (var i = 0; i < numbers.Length; i++)
        {
            if (!TryParseNumber(numbers[i], out parts[i]))
                return false;
        }

        wasPartial = numbers.Length < 3;
        version = new SemanticVersion(parts[0], parts[1], parts[2], preRelease);
        return true;
    }

    private static bool TryParseNumber(string text, out int value)
    {
        value = 0;
        if (text.Length == 0 || (text.Length > 1 && text[0] == '0') || !text.All(char.IsAsciiDigit))
            return false;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsValidPreReleaseIdentifier(string identifier)
    {
        if (identifier.Length == 0 || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            return false;
        var numeric = identifier.All(char.IsAsciiDigit);
        return !numeric || identifier.Length == 1 || identifier[0] != '0';
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
            return 1;

        var result = Major.CompareTo(other.Major);
        if (result == 0)
            result = Minor.CompareTo(other.Minor);
        if (result == 0)
            result = Patch.CompareTo(other.Patch);
        return result != 0 ? result : ComparePreRelease(_preRelease, other._preRelease);
    }

    /// <summary>Compares only major, minor and patch, ignoring the pre-release part.</summary>
    public bool HasSameNumbers(SemanticVersion other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    private static int ComparePreRelease(string[] left, string[] right)
    {
        if (left.Length == 0 || right.Length == 0)
            return right.Length.CompareTo(left.Length);

        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var result = CompareIdentifier(left[i], right[i]);
            if (result != 0)
                return result;
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = left.All(char.IsAsciiDigit);
        var rightNumeric = right.All(char.IsAsciiDigit);
        if (leftNumeric && rightNumeric)
            return left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
        if (leftNumeric != rightNumeric)
            return leftNumeric ? -1 : 1;
        return string.CompareOrdinal(left, right);
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Major);
        hash.Add(Minor);
        hash.Add(Patch);
        foreach (var identifier in _preRelease)
            hash.Add(identifier, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        var core = string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
        return IsPreRelease ? core + "-" + string.Join('.', _preRelease) : core;
    }

    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) => left?.Equals(right) ?? right is null;

    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
}
