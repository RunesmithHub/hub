namespace RunesmithHub.Protocol.Index;

/// <summary>The codes of the conditions that make a version need review.</summary>
public static class ReviewTriggers
{
    public const string FirstVersion = "first-version";
    public const string CapabilityAdded = "capability-added";
    public const string DynamicCode = "dynamic-code";
    public const string NativeCode = "native-code";
    public const string NewNuGetPackage = "new-nuget-package";
    public const string MaintainersChanged = "maintainers-changed";
    public const string Heuristic = "heuristic";
    public const string BuildLogic = "build-logic";
    public const string SizeGrowth = "size-growth";
    public const string ReleaseAuthor = "release-author";
    public const string NameChanged = "name-changed";
    public const string PreviousRestricted = "previous-restricted";
    public const string Tier = "tier";
    public const string License = "license";
    public const string ContractsContent = "contracts-content";
    public const string NetworkHostAdded = "network-host-added";
    public const string WebContent = "web-content";

    public static IReadOnlyList<string> All { get; } =
    [
        FirstVersion, CapabilityAdded, DynamicCode, NativeCode, NewNuGetPackage, MaintainersChanged, Heuristic, BuildLogic, SizeGrowth,
        ReleaseAuthor, NameChanged, PreviousRestricted, Tier, License, ContractsContent, NetworkHostAdded, WebContent,
    ];
}
