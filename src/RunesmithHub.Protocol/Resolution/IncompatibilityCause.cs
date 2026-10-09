using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Resolution;

/// <summary>Why an incompatibility holds: a fact from the inputs, or a conclusion from two other incompatibilities.</summary>
internal abstract record IncompatibilityCause;

/// <summary>The resolution must select the root.</summary>
internal sealed record RootCause : IncompatibilityCause;

/// <summary>Two incompatibilities together imply this one.</summary>
internal sealed record ConflictCause(Incompatibility Left, Incompatibility Right) : IncompatibilityCause;

/// <summary>Something the user has or asked for requires a plugin.</summary>
internal sealed record RequirementCause(string Package, RequirementKind Kind, SemanticVersion? Version) : IncompatibilityCause;

/// <summary>The user asked to remove a plugin.</summary>
internal sealed record RemovalCause(string Package) : IncompatibilityCause;

/// <summary>Versions of one plugin depend on another plugin with a range.</summary>
internal sealed record DependencyCause(string Dependent, string Dependency, VersionRange Range) : IncompatibilityCause;

/// <summary>A dependent's tier does not allow some versions of its dependency.</summary>
internal sealed record TrustCause(string Dependent, string Dependency, TrustProblem Problem) : IncompatibilityCause;

/// <summary>Versions of a plugin can't be chosen at all.</summary>
internal sealed record UnavailableCause(string Package, Unavailability Reason) : IncompatibilityCause;

internal enum RequirementKind
{
    Install,
    Installed,
    Update,
}

internal enum TrustProblem
{
    /// <summary>The dependency's tier is below what the dependent's tier may depend on.</summary>
    Tier,

    /// <summary>The dependency's versions have not been reviewed by a person.</summary>
    NotReviewed,
}

/// <summary>Why a version can't be chosen, in the order the reasons are checked.</summary>
internal enum Unavailability
{
    Blocked,
    TierSetting,
    Api,
    Platform,
    Frozen,
    Deleted,
    Yanked,
    InReview,
    Held,
}

internal static class UnavailabilityExtensions
{
    public static Unavailability FromState(VersionState state) => state switch
    {
        VersionState.Blocked => Unavailability.Blocked,
        VersionState.Frozen => Unavailability.Frozen,
        VersionState.Deleted => Unavailability.Deleted,
        VersionState.Yanked => Unavailability.Yanked,
        VersionState.Held => Unavailability.Held,
        _ => Unavailability.InReview,
    };
}
