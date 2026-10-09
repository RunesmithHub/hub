using System.Net;
using RunesmithHub.Protocol.Tests.Support;
using RunesmithHub.Protocol.Updating;

namespace RunesmithHub.Protocol.Tests.Updating;

public sealed class FetcherTests
{
    private static readonly Uri BaseUrl = new("https://index.example/registry/");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private static async Task<FetchResult> FetchAsync(Func<HttpRequestMessage, HttpResponseMessage> respond, string path = "timestamp.json", long limit = 100)
    {
        using var handler = new StubHandler(respond);
        using var client = new HttpClient(handler);
        return await new HttpIndexFetcher(client, BaseUrl).FetchAsync(path, limit, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetsAFileRelativeToTheBaseUrl()
    {
        Uri? requested = null;

        var result = await FetchAsync(request =>
        {
            requested = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("{}"u8.ToArray()) };
        }, "root/2.json");

        Assert.Equal(FetchStatus.Found, result.Status);
        Assert.Equal("{}"u8.ToArray(), result.Content.ToArray());
        Assert.Equal(new Uri("https://index.example/registry/root/2.json"), requested);
    }

    [Fact]
    public async Task ReportsMissingAndOversizedFiles()
    {
        var missing = await FetchAsync(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var declaredTooLarge = await FetchAsync(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[101]) });
        var streamedTooLarge = await FetchAsync(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[500])) });

        Assert.Equal(FetchStatus.NotFound, missing.Status);
        Assert.Equal(FetchStatus.TooLarge, declaredTooLarge.Status);
        Assert.Equal(FetchStatus.TooLarge, streamedTooLarge.Status);
    }

    [Fact]
    public async Task TreatsServerErrorsAsTheIndexBeingUnavailable()
    {
        await Assert.ThrowsAsync<IndexUnavailableException>(() => FetchAsync(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<IndexUnavailableException>(() => FetchAsync(_ => throw new HttpRequestException("No route to host.")));
    }

    [Theory]
    [InlineData("http://index.example/registry/")]
    [InlineData("https://index.example/registry")]
    public void AcceptsOnlyHttpsBaseUrlsEndingInASlash(string url)
    {
        using var client = new HttpClient();

        Assert.Throws<ArgumentException>(() => new HttpIndexFetcher(client, new Uri(url)));
    }

    [Fact]
    public void AcceptsPlainHttpOnLoopback()
    {
        using var client = new HttpClient();

        _ = new HttpIndexFetcher(client, new Uri("http://localhost:8080/"));
    }

    [Fact]
    public async Task ReadsFilesFromAFolderWithinTheLimit()
    {
        using var folder = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "root"));
        await File.WriteAllBytesAsync(Path.Combine(folder.Path, "root", "1.json"), new byte[10], TestContext.Current.CancellationToken);
        var fetcher = new DirectoryIndexFetcher(folder.Path);

        Assert.Equal(FetchStatus.Found, (await fetcher.FetchAsync("root/1.json", 10, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(FetchStatus.TooLarge, (await fetcher.FetchAsync("root/1.json", 9, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(FetchStatus.NotFound, (await fetcher.FetchAsync("root/2.json", 10, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(FetchStatus.NotFound, (await fetcher.FetchAsync("../secret.json", 10, TestContext.Current.CancellationToken)).Status);
    }
}
