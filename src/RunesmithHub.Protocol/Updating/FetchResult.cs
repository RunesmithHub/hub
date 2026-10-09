namespace RunesmithHub.Protocol.Updating;

/// <summary>The outcome of fetching one index file.</summary>
public sealed record FetchResult
{
    private FetchResult(FetchStatus status, ReadOnlyMemory<byte> content)
    {
        Status = status;
        Content = content;
    }

    public static FetchResult NotFound { get; } = new(FetchStatus.NotFound, default);

    public static FetchResult TooLarge { get; } = new(FetchStatus.TooLarge, default);

    public FetchStatus Status { get; }

    /// <summary>Gets the file's bytes when it was found.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    public static FetchResult Found(ReadOnlyMemory<byte> content) => new(FetchStatus.Found, content);
}
