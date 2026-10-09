using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.GitHub;

public sealed class GitHubRestClientTests : IDisposable
{
    private readonly ScriptedHandler _handler = new();
    private readonly HttpClient _http;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly TemporaryFolder _folder = new();

    public GitHubRestClientTests() => _http = new HttpClient(_handler);

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
        _folder.Dispose();
    }

    private GitHubRestClient Client(IResponseCache? cache = null) => new(_http, "secret-token", cache ?? NoResponseCache.Instance, _clock);

    [Fact]
    public async Task SendsTheTokenAndApiVersionOnlyToGitHub()
    {
        _handler.Respond(HttpStatusCode.OK, "{\"id\":1}");

        await Client().GetAsync("repositories/1", TestContext.Current.CancellationToken);

        var request = _handler.Requests.Single();
        Assert.Equal("https://api.github.com/repositories/1", request.Url);
        Assert.Equal("Bearer secret-token", request.Authorization);
        Assert.Equal("2022-11-28", request.ApiVersion);
    }

    [Fact]
    public async Task AnswersNotModifiedFromTheCacheWithoutCountingAgainstTheLimit()
    {
        var cache = new FileResponseCache(_folder.Combine("cache.json"));
        _handler.Respond(HttpStatusCode.OK, "[{\"id\":1}]", etag: "\"abc\"");
        _handler.Respond(HttpStatusCode.NotModified, "");

        var first = await Client(cache).GetAllPagesAsync("repos/o/r/releases", TestContext.Current.CancellationToken);
        cache.Save();
        var second = await Client(new FileResponseCache(_folder.Combine("cache.json"))).GetAllPagesAsync("repos/o/r/releases", TestContext.Current.CancellationToken);

        Assert.Single(first);
        Assert.Equal(1, second.Single().GetProperty("id").GetInt32());
        Assert.Equal("\"abc\"", _handler.Requests[1].IfNoneMatch);
    }

    [Fact]
    public async Task FollowsNextLinksAcrossPages()
    {
        _handler.Respond(HttpStatusCode.OK, "[{\"n\":1},{\"n\":2}]", link: "<https://api.github.com/repos/o/r/releases?per_page=100&page=2>; rel=\"next\"");
        _handler.Respond(HttpStatusCode.OK, "[{\"n\":3}]");

        var items = await Client().GetAllPagesAsync("repos/o/r/releases", TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 3], items.Select(item => item.GetProperty("n").GetInt32()));
        Assert.EndsWith("page=2", _handler.Requests[1].Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitsForAShortRateLimitResetAndRetries()
    {
        var reset = _clock.GetUtcNow().AddSeconds(30).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        _handler.Respond(HttpStatusCode.Forbidden, "{\"message\":\"rate limited\"}", remaining: "0", reset: reset);
        _handler.Respond(HttpStatusCode.OK, "{\"id\":7}", remaining: "4999");
        var client = Client();

        var pending = client.GetAsync("repositories/7", TestContext.Current.CancellationToken);
        while (!pending.IsCompleted && _handler.Requests.Count < 2)
        {
            _clock.Advance(TimeSpan.FromSeconds(31));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(7, (await pending)!.Value.GetProperty("id").GetInt32());
        Assert.Equal(4999, client.RateLimitRemaining);
    }

    [Fact]
    public async Task GivesUpWhenTheRateLimitResetsTooLate()
    {
        var reset = _clock.GetUtcNow().AddMinutes(30).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        _handler.Respond(HttpStatusCode.Forbidden, "{\"message\":\"rate limited\"}", remaining: "0", reset: reset);

        var exception = await Assert.ThrowsAsync<GitHubException>(() => Client().GetAsync("repositories/7", TestContext.Current.CancellationToken));

        Assert.Contains("rate limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsNullForMissingResourcesAndThrowsForErrors()
    {
        _handler.Respond(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");
        _handler.Respond(HttpStatusCode.UnprocessableEntity, "{\"message\":\"Validation Failed\"}");

        Assert.Null(await Client().GetAsync("repositories/404", TestContext.Current.CancellationToken));
        var exception = await Assert.ThrowsAsync<GitHubException>(() => Client().SendJsonAsync(HttpMethod.Post, "repos/o/r/issues", null, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.Status);
        Assert.Contains("Validation Failed", exception.Message, StringComparison.Ordinal);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public List<(string Url, string? Authorization, string? ApiVersion, string? IfNoneMatch)> Requests { get; } = [];

        public void Respond(HttpStatusCode status, string body, string? etag = null, string? link = null, string? remaining = null, string? reset = null) =>
            _responses.Enqueue(() =>
            {
                var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                if (etag is not null)
                    response.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(etag);
                if (link is not null)
                    response.Headers.Add("Link", link);
                if (remaining is not null)
                    response.Headers.Add("x-ratelimit-remaining", remaining);
                if (reset is not null)
                    response.Headers.Add("x-ratelimit-reset", reset);
                return response;
            });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-GitHub-Api-Version", out var versions) ? versions.Single() : null,
                request.Headers.IfNoneMatch.FirstOrDefault()?.ToString()));
            var response = _responses.Dequeue()();
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
