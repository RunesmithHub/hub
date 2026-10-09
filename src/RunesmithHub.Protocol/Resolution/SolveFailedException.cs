namespace RunesmithHub.Protocol.Resolution;

internal sealed class SolveFailedException(Incompatibility incompatibility) : Exception("No solution exists.")
{
    public Incompatibility Incompatibility { get; } = incompatibility;
}
