namespace RunesmithHub.Protocol.Resolution;

/// <summary>Why no install plan exists, in plain sentences.</summary>
/// <param name="Message">The explanation users see, with a suggested change when one exists.</param>
/// <param name="Derivation">Every step of the reasoning, one sentence each, for details and logs.</param>
/// <param name="Dependents">For <see cref="ResolutionFailureKind.NeededByOthers"/>, the installed plugins that need what is being removed.</param>
public sealed record ResolutionFailure(ResolutionFailureKind Kind, string Message, IReadOnlyList<string> Derivation, IReadOnlyList<string> Dependents);
