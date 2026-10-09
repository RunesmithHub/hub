namespace RunesmithHub.Protocol.Index;

/// <summary>A downloadable file pinned by hash and length: a package, a source archive or an SBOM.</summary>
public sealed record HostedFile
{
    /// <summary>Gets the URLs the file can be downloaded from, in order of preference.</summary>
    public required IReadOnlyList<string> Urls { get; init; }

    public required string Sha256 { get; init; }

    public required long Length { get; init; }
}
