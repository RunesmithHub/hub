namespace RunesmithHub.Tool;

/// <summary>Downloads public files by URL.</summary>
internal interface IDownloader
{
    /// <summary>Downloads a file, or returns null when it does not exist.</summary>
    /// <exception cref="ToolException">The file is larger than allowed.</exception>
    Task<byte[]?> GetAsync(Uri url, long maxBytes, CancellationToken cancellationToken);
}

/// <summary>Downloads over HTTPS with a size limit.</summary>
internal sealed class HttpDownloader(HttpClient http) : IDownloader
{
    public async Task<byte[]?> GetAsync(Uri url, long maxBytes, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new ToolException($"{url} is larger than {maxBytes} bytes.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
