using System.Globalization;

namespace RunesmithHub.Protocol.Versioning;

/// <summary>
/// A set of mutually compatible versions: one major version from 1.0.0 on, one minor version in <c>0.y</c>, one patch in <c>0.0.z</c>.
/// </summary>
public readonly record struct CompatibilityLine
{
    private CompatibilityLine(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }

    /// <summary>Gets the minor number that defines the line, or zero when the major number does.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch number that defines the line, or zero when the major or minor number does.</summary>
    public int Patch { get; }

    /// <summary>Gets the first release of the line, such as <c>1.0.0</c>, <c>0.3.0</c> or <c>0.0.3</c>.</summary>
    public SemanticVersion Start => new(Major, Minor, Patch);

    /// <summary>Gets the first release of the next line, such as <c>2.0.0</c>, <c>0.4.0</c> or <c>0.0.4</c>.</summary>
    public SemanticVersion NextStart => Next.Start;

    /// <summary>Gets the line that follows this one.</summary>
    public CompatibilityLine Next => Major > 0 ? new(Major + 1, 0, 0) : Minor > 0 ? new(0, Minor + 1, 0) : new(0, 0, Patch + 1);

    /// <summary>Gets the line of a version; pre-releases belong to the line of their numbers.</summary>
    public static CompatibilityLine Of(SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return version.Major > 0 ? new(version.Major, 0, 0) : version.Minor > 0 ? new(0, version.Minor, 0) : new(0, 0, version.Patch);
    }

    /// <summary>Gets whether a version belongs to this line.</summary>
    public bool Contains(SemanticVersion version) => Of(version) == this;

    /// <summary>Returns the line as people read it: <c>1.x</c>, <c>0.3</c> or <c>0.0.3</c>.</summary>
    public override string ToString() =>
        Major > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.x")
            : Minor > 0
                ? string.Create(CultureInfo.InvariantCulture, $"0.{Minor}")
                : string.Create(CultureInfo.InvariantCulture, $"0.0.{Patch}");
}
