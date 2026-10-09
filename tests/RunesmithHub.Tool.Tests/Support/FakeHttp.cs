using System.Net;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>Answers HTTP requests from canned responses by URL, and records every request with its body.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    public Dictionary<string, (HttpStatusCode Status, byte[] Body)> Responses { get; } = new(StringComparer.Ordinal);

    public List<(string Url, string? Body)> Requests { get; } = [];

    public FakeHttp Answer(string url, byte[] body, HttpStatusCode status = HttpStatusCode.OK)
    {
        Responses[url] = (status, body);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        Requests.Add((url, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        var (status, body) = Responses.TryGetValue(url, out var response) ? response : (HttpStatusCode.NotFound, []);
        return new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
    }
}
