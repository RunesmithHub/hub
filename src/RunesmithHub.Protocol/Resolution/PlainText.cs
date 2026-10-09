using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Resolution;

/// <summary>Writes ranges, version sets and lists the way people read them.</summary>
internal static class PlainText
{
    /// <summary>Describes a range: <c>0.3</c>, <c>0.3.1 or later in 0.3</c>, <c>1.4.2</c>, <c>at least 1.4.0 and below 1.9.0</c>.</summary>
    public static string Range(VersionRange range) => string.Join(" or ", range.Normalize().Sets.Select(Interval));

    private static string Interval(VersionInterval set)
    {
        if (set.IsExact)
            return set.Lower!.Version.ToString();

        if (set is { Lower: { Inclusive: true } lower, Upper: { Inclusive: false } upper } && !lower.Version.IsPreRelease && upper.Version == lower.Version.Line.NextStart)
        {
            var line = lower.Version.Line;
            return lower.Version == line.Start ? line.ToString() : $"{lower.Version} or later in {line}";
        }

        var parts = new List<string>(2);
        if (set.Lower is { } low)
            parts.Add((low.Inclusive ? "at least " : "above ") + low.Version);
        if (set.Upper is { } high)
            parts.Add((high.Inclusive ? "up to " : "below ") + high.Version);
        return string.Join(" and ", parts);
    }

    /// <summary>Describes some of a package's versions, or returns an empty string when the set holds all of them.</summary>
    public static string Versions(PackageVersions package, VersionSet set)
    {
        if (set.IsFull)
            return "";

        var runs = new List<(int First, int Last)>();
        foreach (var index in set.Indexes())
        {
            if (runs.Count > 0 && runs[^1].Last == index - 1)
                runs[^1] = (runs[^1].First, index);
            else
                runs.Add((index, index));
        }

        return List(runs.Select(run => run.First == run.Last
            ? package.Versions[run.First].Version.ToString()
            : $"{package.Versions[run.First].Version} to {package.Versions[run.Last].Version}"), "or");
    }

    /// <summary>Joins items as <c>A</c>, <c>A and B</c> or <c>A, B and C</c>.</summary>
    public static string List(IEnumerable<string> items, string conjunction = "and")
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + $" {conjunction} " + list[^1],
        };
    }

    public static string Tier(PluginTier tier) => tier switch
    {
        PluginTier.Official => "official",
        PluginTier.Verified => "verified",
        _ => "unverified",
    };

    public static string Allowed(AllowedTiers tiers) => tiers == AllowedTiers.OfficialOnly ? "official" : "official and verified";

    public static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
