using System.Net;

namespace RunesmithHub.Protocol.Updating;

/// <summary>
/// Gets index files over HTTPS from a base URL ending in <c>/</c>. Plain HTTP is accepted only for loopback hosts, for local development.
/// Redirect limits are the <see cref="HttpClient"/>'s handler settings.
/// </summary>
public sealed class HttpIndexFetcher : IIndexFetcher
{
    private readonly HttpClient _client;
    private readonly Uri _baseUrl;

    /// <exception cref="ArgumentException">The base URL is not HTTPS or does not end in <c>/</c>.</exception>
    public HttpIndexFetcher(HttpClient client, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(baseUrl);
        if (!baseUrl.IsAbsoluteUri || !baseUrl.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("The index base URL must be absolute and end in /.", nameof(baseUrl));
        if (baseUrl.Scheme != Uri.UriSchemeHttps && !(baseUrl.Scheme == Uri.UriSchemeHttp && baseUrl.IsLoopback))
            throw new ArgumentException("The index base URL must use HTTPS.", nameof(baseUrl));

        _client = client;
        _baseUrl = baseUrl;
    }

    public async Task<FetchResult> FetchAsync(string path, long maxBytes, CancellationToken cancellationToken)
    {
        if (!RelativePaths.IsSafe(path))
            return FetchResult.NotFound;

        try
        {
            using var response = await _client.GetAsync(new Uri(_baseUrl, path), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return FetchResult.NotFound;
            if (!response.IsSuccessStatusCode)
                throw new IndexUnavailableException($"The index answered {(int)response.StatusCode} for {path}.");
            if (response.Content.Headers.ContentLength > maxBytes)
                return FetchResult.TooLarge;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await LimitedReader.ReadAsync(stream, maxBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new IndexUnavailableException($"The index could not be reached: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IndexUnavailableException("The index did not answer in time.", exception);
        }
    }
}
