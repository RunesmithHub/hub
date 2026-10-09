using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RunesmithHub.Tool.GitHub;

/// <summary>
/// A small client for GitHub's REST and GraphQL APIs: conditional requests with entity tags, paging through <c>Link</c> headers, and
/// waiting out short rate-limit resets instead of failing.
/// </summary>
internal sealed partial class GitHubRestClient
{
    private static readonly Uri ApiBase = new("https://api.github.com/");
    private static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly string? _token;
    private readonly IResponseCache _cache;
    private readonly TimeProvider _time;
    private DateTimeOffset? _exhaustedUntil;

    public GitHubRestClient(HttpClient http, string? token, IResponseCache cache, TimeProvider time)
    {
        _http = http;
        _token = string.IsNullOrEmpty(token) ? null : token;
        _cache = cache;
        _time = time;
    }

    /// <summary>Gets how many requests the rate limit still allows, as of the last response.</summary>
    public int? RateLimitRemaining { get; private set; }

    /// <summary>Gets a JSON resource, or null when it does not exist.</summary>
    public async Task<JsonElement?> GetAsync(string path, CancellationToken cancellationToken)
    {
        var page = await GetPageAsync(Resolve(path), cancellationToken).ConfigureAwait(false);
        return page?.Body;
    }

    /// <summary>Gets every item of a paged JSON array, 100 per page.</summary>
    /// <param name="selector">Picks the array out of each page; by default the page is the array.</param>
    public async Task<IReadOnlyList<JsonElement>> GetAllPagesAsync(string path, CancellationToken cancellationToken, Func<JsonElement, JsonElement>? selector = null)
    {
        var items = new List<JsonElement>();
        var url = Resolve(path + (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "per_page=100");
        while (url is not null)
        {
            var page = await GetPageAsync(url, cancellationToken).ConfigureAwait(false);
            if (page is null)
                break;
            var array = selector is null ? page.Body : selector(page.Body);
            if (array.ValueKind == JsonValueKind.Array)
                items.AddRange(array.EnumerateArray());
            url = page.Next is null ? null : new Uri(page.Next);
        }

        return items;
    }

    /// <summary>Gets raw bytes, such as a file's content or an artifact archive, or null when it does not exist.</summary>
    public async Task<byte[]?> GetBytesAsync(string path, string accept, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => Request(HttpMethod.Get, Resolve(path), accept), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends a request with an optional JSON body and returns the JSON answer, or an empty object when there is none.</summary>
    /// <exception cref="GitHubException">GitHub answered with an error.</exception>
    public async Task<JsonElement> SendJsonAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() =>
        {
            var request = Request(method, Resolve(path), "application/vnd.github+json");
            if (body is not null)
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            return request;
        }, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Uploads bytes to an upload URL, such as a release's asset endpoint.</summary>
    public async Task<JsonElement> UploadAsync(Uri url, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() =>
        {
            var request = Request(HttpMethod.Post, url, "application/vnd.github+json");
            request.Content = new ByteArrayContent(content);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return request;
        }, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a GraphQL query or mutation.</summary>
    /// <exception cref="GitHubException">GitHub answered with an error or the result has errors.</exception>
    public async Task<JsonElement> GraphQlAsync(string query, JsonObject variables, CancellationToken cancellationToken)
    {
        var result = await SendJsonAsync(HttpMethod.Post, "graphql", new JsonObject { ["query"] = query, ["variables"] = variables }, cancellationToken).ConfigureAwait(false);
        if (result.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            throw new GitHubException($"GitHub's GraphQL API answered: {errors[0].GetProperty("message").GetString()}");
        return result.GetProperty("data");
    }

    private sealed record Page(JsonElement Body, string? Next);

    private async Task<Page?> GetPageAsync(Uri url, CancellationToken cancellationToken)
    {
        var key = url.ToString();
        var cached = _cache.Get(key);
        using var response = await SendAsync(() =>
        {
            var request = Request(HttpMethod.Get, url, "application/vnd.github+json");
            if (cached is not null)
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cached.ETag.StartsWith("W/", StringComparison.Ordinal) ? cached.ETag[2..] : cached.ETag, cached.ETag.StartsWith("W/", StringComparison.Ordinal)));
            return request;
        }, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            return new Page(Parse(cached.Body), cached.Next);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var next = NextLink(response);
        if (response.Headers.ETag is { } etag)
            _cache.Set(key, new CachedResponse(etag.ToString(), body, next));
        return new Page(Parse(body), next);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> create, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (_exhaustedUntil is { } until)
                await WaitUntilAsync(until, cancellationToken).ConfigureAwait(false);

            using var request = create();
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            TrackRateLimit(response);
            var wait = RetryAfter(response);
            if (wait is null || attempt > 0)
                return response;

            response.Dispose();
            await WaitUntilAsync(_time.GetUtcNow() + wait.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private void TrackRateLimit(HttpResponseMessage response)
    {
        if (Header(response, "x-ratelimit-remaining") is { } remaining && int.TryParse(remaining, CultureInfo.InvariantCulture, out var count))
        {
            RateLimitRemaining = count;
            _exhaustedUntil = count == 0 && Header(response, "x-ratelimit-reset") is { } reset && long.TryParse(reset, CultureInfo.InvariantCulture, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
            return null;
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return delta;
        return _exhaustedUntil is { } until ? until - _time.GetUtcNow() : null;
    }

    private async Task WaitUntilAsync(DateTimeOffset until, CancellationToken cancellationToken)
    {
        var wait = until - _time.GetUtcNow();
        if (wait <= TimeSpan.Zero)
        {
            _exhaustedUntil = null;
            return;
        }

        if (wait > LongestWait)
            throw new GitHubException(string.Create(CultureInfo.InvariantCulture, $"GitHub's rate limit is used up until {until:u}; try again later."));
        await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
        _exhaustedUntil = null;
    }

    private HttpRequestMessage Request(HttpMethod method, Uri url, string accept)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.UserAgent.ParseAdd("rshub/0.1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (_token is not null && url.Host is "api.github.com" or "uploads.github.com")
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return request;
    }

    private static Uri Resolve(string path) => path.StartsWith("https://", StringComparison.Ordinal) ? new Uri(path) : new Uri(ApiBase, path);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var message = body;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var text))
                message = text.GetString() ?? body;
        }
        catch (JsonException)
        {
        }

        throw new GitHubException(response.StatusCode, $"GitHub answered {(int)response.StatusCode} for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}: {message}");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Parse(body.Length == 0 ? "{}" : body);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string? NextLink(HttpResponseMessage response) =>
        Header(response, "Link") is { } link && NextLinkPattern().Match(link) is { Success: true } match ? match.Groups[1].Value : null;

    [GeneratedRegex("<([^>]+)>;\\s*rel=\"next\"")]
    private static partial Regex NextLinkPattern();
}
