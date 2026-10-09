using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RunesmithHub.Tool.Analysis.Heuristics;

/// <summary>One malware heuristic: what it looks at and what counts as a match. Only the fields its kind uses are set.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record HeuristicRule
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public required string Id { get; init; }

    public required HeuristicKind Kind { get; init; }

    /// <summary>Gets a .NET regular expression.</summary>
    public string? Pattern { get; init; }

    public long? MinBytes { get; init; }

    public int? MinLength { get; init; }

    public double? MinBitsPerByte { get; init; }

    /// <summary>Gets file signatures as hex, such as <c>4D5A</c>.</summary>
    public IReadOnlyList<string>? Signatures { get; init; }

    public IReadOnlyList<string>? Names { get; init; }

    public IReadOnlyList<string>? Apis { get; init; }

    /// <summary>Gets the rule's regular expression, which gives up on any one text after a second.</summary>
    public Regex? Regex => Pattern is null ? null : new Regex(Pattern, RegexOptions.CultureInvariant, MatchTimeout);

    /// <summary>Gets what the rule's kind needs but the rule lacks, or null when it is complete.</summary>
    public string? Problem()
    {
        var missing = Kind switch
        {
            HeuristicKind.String when Pattern is null => "pattern",
            HeuristicKind.Resource when Pattern is null && MinBytes is null => "pattern or minBytes",
            HeuristicKind.Entropy when MinLength is null || MinBitsPerByte is null => "minLength and minBitsPerByte",
            HeuristicKind.Magic when Signatures is not { Count: > 0 } => "signatures",
            HeuristicKind.Attribute when Names is not { Count: > 0 } => "names",
            HeuristicKind.ApiCombination when Apis is not { Count: > 0 } => "apis",
            _ => null,
        };
        if (missing is not null)
            return $"Rule {Id} needs {missing}.";
        try
        {
            _ = Regex;
            _ = Signatures?.Select(Convert.FromHexString).ToList();
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Rule {Id} is not valid: {exception.Message}");
        }
    }
}

/// <summary>The heuristic rules file: a JSON object with a <c>rules</c> array.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record HeuristicRuleSet(IReadOnlyList<HeuristicRule> Rules)
{
    /// <summary>The environment variable naming the rules file when <c>--rules</c> is not given.</summary>
    public const string FileVariable = "HUB_HEURISTICS_FILE";

    /// <exception cref="ToolException">The file is not a valid rules file.</exception>
    public static HeuristicRuleSet Read(string path)
    {
        HeuristicRuleSet rules;
        try
        {
            rules = Json.ArtifactJson.Deserialize<HeuristicRuleSet>(File.ReadAllBytes(path));
        }
        catch (InvalidDataException exception)
        {
            throw new ToolException($"The heuristic rules in {path} are not valid: {exception.Message}", exception);
        }

        var problems = rules.Rules.Select(rule => rule.Problem()).OfType<string>().ToList();
        if (rules.Rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != rules.Rules.Count)
            problems.Add("Rule IDs must be unique.");
        return problems.Count == 0 ? rules : throw new ToolException($"The heuristic rules in {path} are not valid: {string.Join(" ", problems)}");
    }
}
