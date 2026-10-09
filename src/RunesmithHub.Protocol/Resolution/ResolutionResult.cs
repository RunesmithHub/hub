using System.Diagnostics.CodeAnalysis;

namespace RunesmithHub.Protocol.Resolution;

/// <summary>An install plan, or why there is none.</summary>
public sealed record ResolutionResult
{
    public InstallPlan? Plan { get; init; }

    public ResolutionFailure? Failure { get; init; }

    [MemberNotNullWhen(true, nameof(Plan))]
    [MemberNotNullWhen(false, nameof(Failure))]
    public bool Succeeded => Plan is not null;
}
