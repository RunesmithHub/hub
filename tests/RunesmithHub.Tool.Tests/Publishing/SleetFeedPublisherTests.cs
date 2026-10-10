using System.Text.Json.Nodes;
using RunesmithHub.Tool.Processes;
using RunesmithHub.Tool.Publishing;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Publishing;

public sealed class SleetFeedPublisherTests
{
    private static readonly Dictionary<string, string> Environment = new(StringComparer.Ordinal)
    {
        ["HUB_FEED_URL"] = "https://nuget.example.test/index.json",
        ["HUB_FEED_ACCOUNT_ID"] = "account",
        ["HUB_FEED_BUCKET"] = "bucket",
        ["HUB_FEED_ACCESS_KEY_ID"] = "key-id",
        ["HUB_FEED_SECRET_ACCESS_KEY"] = "secret",
    };

    [Fact]
    public async Task WritesSettingsSleetAcceptsForR2()
    {
        var sleet = new CapturingSleet();
        using var http = new HttpClient(new FakeHttp());
        var publisher = new SleetFeedPublisher(sleet, http, name => Environment.GetValueOrDefault(name));
        var package = Path.GetTempFileName();
        try
        {
            await publisher.PushAsync(package, "RunesmithHub.Lumen.Todo.Contracts", "1.0.0", new string('0', 64), TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(package);
        }

        var source = sleet.Settings!["sources"]![0]!.AsObject();
        Assert.Equal("s3", (string?)source["type"]);
        Assert.Equal("https://account.r2.cloudflarestorage.com", (string?)source["serviceURL"]);
        Assert.Equal("https://nuget.example.test/", (string?)source["path"]);
        Assert.Equal("https://nuget.example.test/", (string?)source["baseURI"]);
        Assert.True((bool?)source["disablePayloadSigning"]);
        Assert.False(source.ContainsKey("region"));
    }

    private sealed class CapturingSleet : IProcessRunner
    {
        public JsonNode? Settings { get; private set; }

        public Task<ProcessResult> RunAsync(
            string program, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken = default) =>
            RunAsync(program, arguments, workingDirectory, cancellationToken);

        public async Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken = default)
        {
            var config = arguments[arguments.ToList().IndexOf("--config") + 1];
            Settings = JsonNode.Parse(await File.ReadAllTextAsync(config, cancellationToken));
            return new ProcessResult(0, "", "");
        }
    }
}
