namespace RunesmithHub.Protocol.Index;

/// <summary>Pins a file by its document version, SHA-256 and length in bytes.</summary>
public sealed record FileMeta
{
    public required int Version { get; init; }

    public required string Sha256 { get; init; }

    public required long Length { get; init; }

    /// <summary>Describes a file's bytes.</summary>
    public static FileMeta Of(int version, ReadOnlySpan<byte> bytes) => new() { Version = version, Sha256 = Hashes.Sha256(bytes), Length = bytes.Length };
}
