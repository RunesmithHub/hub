namespace RunesmithHub.Protocol.Index;

/// <summary>A version's contracts assembly and the NuGet package that carries it.</summary>
public sealed record ContractsPackage
{
    /// <summary>Gets the assembly file name, such as <c>Lumen.Todo.Contracts.dll</c>.</summary>
    public required string Assembly { get; init; }

    public required string Sha256 { get; init; }

    /// <summary>Gets the NuGet package ID, such as <c>RunesmithHub.Lumen.Todo.Contracts</c>.</summary>
    public required string Package { get; init; }

    public required string PackageVersion { get; init; }
}
