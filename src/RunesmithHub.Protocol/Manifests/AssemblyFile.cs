namespace RunesmithHub.Protocol.Manifests;

/// <summary>An assembly's file name in <c>lib/</c> and its SHA-256.</summary>
public sealed record AssemblyFile
{
    public required string File { get; init; }

    public required string Sha256 { get; init; }
}
