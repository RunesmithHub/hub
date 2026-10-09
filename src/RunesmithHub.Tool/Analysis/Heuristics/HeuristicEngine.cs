using System.Text.RegularExpressions;

namespace RunesmithHub.Tool.Analysis.Heuristics;

/// <summary>Runs heuristic rules over a package. A match means a person must look, not that the plugin is malicious.</summary>
internal static class HeuristicEngine
{
    private const int ExcerptLength = 80;

    public static IReadOnlyList<HeuristicMatch> Run(HeuristicRuleSet rules, PackageSnapshot package)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var material = ScannedMaterial.Collect(package);
        return [.. rules.Rules.SelectMany(rule => Run(rule, material))];
    }

    private static IEnumerable<HeuristicMatch> Run(HeuristicRule rule, ScannedMaterial material) => Run(rule, rule.Regex, material);

    private static IEnumerable<HeuristicMatch> Run(HeuristicRule rule, Regex? regex, ScannedMaterial material) => rule.Kind switch
    {
        HeuristicKind.String => material.Strings
            .Where(text => IsMatch(regex!, text.Text))
            .Select(text => new HeuristicMatch(rule.Id, text.File, text.Location, Excerpt(text.Text))),
        HeuristicKind.Resource => material.Blobs
            .Where(blob => blob.Location.StartsWith("resource ", StringComparison.Ordinal))
            .Where(blob => (regex is null || IsMatch(regex, blob.Location["resource ".Length..])) && blob.Bytes.LongLength >= (rule.MinBytes ?? 0))
            .Select(blob => new HeuristicMatch(rule.Id, blob.File, blob.Location, $"{blob.Bytes.LongLength} bytes")),
        HeuristicKind.Entropy => material.Strings.Select(text => (text.File, text.Location, Bytes: ScannedMaterial.Bytes(text)))
            .Concat(material.Blobs.Select(blob => (blob.File, blob.Location, blob.Bytes)))
            .Where(item => item.Bytes.Length >= rule.MinLength && Entropy(item.Bytes) >= rule.MinBitsPerByte)
            .Select(item => new HeuristicMatch(rule.Id, item.File, item.Location, $"{item.Bytes.Length} bytes at {Entropy(item.Bytes):F2} bits per byte")),
        HeuristicKind.Magic => material.Blobs
            .Where(blob => rule.Signatures!.Select(Convert.FromHexString).Any(signature => blob.Bytes.AsSpan().StartsWith(signature)))
            .Select(blob => new HeuristicMatch(rule.Id, blob.File, blob.Location, "starts with an executable signature")),
        HeuristicKind.Attribute => material.Names
            .SelectMany(names => names.Value.Where(name => rule.Names!.Contains(name, StringComparer.Ordinal)).Select(name => new HeuristicMatch(rule.Id, names.Key, name, name))),
        HeuristicKind.ApiCombination => material.Apis
            .Where(apis => rule.Apis!.All(pattern => apis.Value.Any(api => Matches(pattern, api))))
            .Select(apis => new HeuristicMatch(rule.Id, apis.Key, apis.Key, string.Join(", ", rule.Apis!))),
        _ => [],
    };

    private static bool IsMatch(Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    private static bool Matches(string pattern, string api) =>
        pattern.EndsWith(".*", StringComparison.Ordinal) ? api.StartsWith(pattern[..^1], StringComparison.Ordinal) : string.Equals(pattern, api, StringComparison.Ordinal);

    /// <summary>Gets the Shannon entropy of bytes, from 0 to 8 bits per byte.</summary>
    public static double Entropy(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return 0;
        Span<int> counts = stackalloc int[256];
        foreach (var value in bytes)
            counts[value]++;
        double entropy = 0;
        foreach (var count in counts)
        {
            if (count == 0)
                continue;
            var probability = count / (double)bytes.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    private static string Excerpt(string text)
    {
        var printable = new string([.. text.Take(ExcerptLength).Select(c => char.IsControl(c) ? ' ' : c)]);
        return text.Length > ExcerptLength ? printable + "..." : printable;
    }
}
