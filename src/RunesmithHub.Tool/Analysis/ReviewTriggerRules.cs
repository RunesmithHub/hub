using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Tool.Analysis;

/// <summary>What the analysis learned that decides which review triggers apply.</summary>
internal sealed record TriggerFacts
{
    public bool FirstVersion { get; init; }

    public IReadOnlyCollection<string> Capabilities { get; init; } = [];

    public IReadOnlyCollection<string> CapabilitiesAdded { get; init; } = [];

    /// <summary>Gets whether the version bundles a NuGet package the previous version did not, or bundles any in a first version.</summary>
    public bool NewPackages { get; init; }

    public bool HeuristicMatched { get; init; }

    public bool BuildLogic { get; init; }

    public bool SizeGrowth { get; init; }

    public bool NameChanged { get; init; }

    public PluginTier Tier { get; init; } = PluginTier.Unverified;

    public IReadOnlyList<AnalysisProblem> Problems { get; init; } = [];
}

/// <summary>
/// Decides the review triggers the analysis can see. Maintainer changes, the release author and the previous version's state are known only
/// to the registry and are added when the version record is written.
/// </summary>
internal static class ReviewTriggerRules
{
    public static IReadOnlyList<string> Triggers(TriggerFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var triggers = new HashSet<string>(StringComparer.Ordinal);
        void When(bool condition, string trigger)
        {
            if (condition)
                triggers.Add(trigger);
        }

        When(facts.FirstVersion, ReviewTriggers.FirstVersion);
        When(facts.CapabilitiesAdded.Count > 0, ReviewTriggers.CapabilityAdded);
        When(facts.Capabilities.Contains(Capabilities.DynamicCode), ReviewTriggers.DynamicCode);
        When(facts.Capabilities.Contains(Capabilities.Native), ReviewTriggers.NativeCode);
        When(facts.NewPackages, ReviewTriggers.NewNuGetPackage);
        When(facts.HeuristicMatched, ReviewTriggers.Heuristic);
        When(facts.BuildLogic, ReviewTriggers.BuildLogic);
        When(facts.SizeGrowth, ReviewTriggers.SizeGrowth);
        When(facts.NameChanged, ReviewTriggers.NameChanged);
        When(facts.Tier != PluginTier.Unverified, ReviewTriggers.Tier);
        When(facts.Problems.Any(problem => problem.Code == ProblemCodes.LicenseCombination), ReviewTriggers.License);
        When(facts.Problems.Any(problem => problem.Code == ProblemCodes.ContractsContent), ReviewTriggers.ContractsContent);
        return [.. ReviewTriggers.All.Where(triggers.Contains)];
    }
}
