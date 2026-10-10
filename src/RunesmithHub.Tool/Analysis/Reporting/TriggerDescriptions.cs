using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Tool.Analysis.Reporting;

/// <summary>What each review trigger means, as reviewers and developers read it.</summary>
internal static class TriggerDescriptions
{
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        [ReviewTriggers.FirstVersion] = "It is the plugin's first version.",
        [ReviewTriggers.CapabilityAdded] = "It declares or uses a capability the previous version did not.",
        [ReviewTriggers.DynamicCode] = "It loads or generates code, or reflects over private members.",
        [ReviewTriggers.NativeCode] = "It runs native code or ships a native library.",
        [ReviewTriggers.NewNuGetPackage] = "It bundles a NuGet package that no earlier version bundled.",
        [ReviewTriggers.MaintainersChanged] = "The plugin's maintainers changed.",
        [ReviewTriggers.Heuristic] = "A review rule matched.",
        [ReviewTriggers.BuildLogic] = "Its build runs commands or custom tasks.",
        [ReviewTriggers.SizeGrowth] = "The package grew much larger than the previous version.",
        [ReviewTriggers.ReleaseAuthor] = "The release was created by someone who is not a maintainer.",
        [ReviewTriggers.NameChanged] = "The display name changed.",
        [ReviewTriggers.PreviousRestricted] = "The previous version was frozen or blocked.",
        [ReviewTriggers.Tier] = "Every version of an official or verified plugin is reviewed.",
        [ReviewTriggers.License] = "A bundled package's license may not combine with the plugin's.",
        [ReviewTriggers.ContractsContent] = "The contracts hold more than a public API needs.",
        [ReviewTriggers.WebContent] = "Its web views run JavaScript or WebAssembly that is new or whose web files changed.",
    };

    public static string Of(string trigger) => Descriptions.GetValueOrDefault(trigger, trigger);
}
