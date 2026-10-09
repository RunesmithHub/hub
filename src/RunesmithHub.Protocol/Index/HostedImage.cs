namespace RunesmithHub.Protocol.Index;

/// <summary>An image the hub re-encoded and hosts.</summary>
public sealed record HostedImage
{
    public required string Url { get; init; }

    public required string Sha256 { get; init; }

    public required long Length { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required ImageFormat Format { get; init; }
}
