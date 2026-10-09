namespace RunesmithHub.Protocol.Updating;

internal static class LimitedReader
{
    /// <summary>Reads a stream to its end, stopping as soon as it passes the limit.</summary>
    public static async Task<FetchResult> ReadAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return FetchResult.Found(buffer.ToArray());
            if (buffer.Length + read > maxBytes)
                return FetchResult.TooLarge;
            buffer.Write(chunk, 0, read);
        }
    }
}
