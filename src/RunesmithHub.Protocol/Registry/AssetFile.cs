namespace RunesmithHub.Protocol.Registry;

/// <summary>A build output file the registry pins by name, SHA-256 and length.</summary>
public sealed record AssetFile
{
    public required string File { get; init; }

    public required string Sha256 { get; init; }

    public required long Length { get; init; }
}
