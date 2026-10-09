namespace RunesmithHub.Protocol.Resolution;

/// <summary>A term the partial solution holds: a decision, or a derivation with the incompatibility that forced it.</summary>
internal sealed record Assignment(Term Term, int DecisionLevel, int Index, Incompatibility? Cause)
{
    public bool IsDecision => Cause is null;
}
