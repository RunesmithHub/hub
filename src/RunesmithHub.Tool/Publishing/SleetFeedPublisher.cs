using System.Net;
using System.Text.Json.Nodes;
using RunesmithHub.Protocol;
using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Publishing;

/// <summary>Changes the hub feed with <c>sleet</c>, with Sleet settings written from the environment into a temporary folder.</summary>
internal sealed class SleetFeedPublisher(IProcessRunner processes, HttpClient http, Func<string, string?> environment) : IFeedPublisher
{
    private const string FeedName = "hub";

    public async Task<FeedPushResult> PushAsync(string packageFile, string packageId, string version, string sha256, CancellationToken cancellationToken)
    {
        if (await ExistingAsync(packageId, version, cancellationToken).ConfigureAwait(false) is { } bytes)
        {
            return Hashes.Matches(bytes, sha256)
                ? FeedPushResult.AlreadyThere
                : throw new ToolException($"The hub feed already has {packageId} {version} with different content.");
        }

        await SleetAsync(["push", Path.GetFullPath(packageFile)], cancellationToken).ConfigureAwait(false);
        return FeedPushResult.Pushed;
    }

    public async Task<bool> DeleteAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        if (await ExistingAsync(packageId, version, cancellationToken).ConfigureAwait(false) is null)
            return false;
        await SleetAsync(["delete", "--id", packageId, "--version", version], cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<byte[]?> ExistingAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        var lowerId = packageId.ToLowerInvariant();
        var lowerVersion = version.ToLowerInvariant();
        using var response = await http.GetAsync(new Uri(FeedBase(), $"flatcontainer/{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg"), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SleetAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var folder = Directory.CreateTempSubdirectory("rshub-sleet-");
        try
        {
            var config = Path.Combine(folder.FullName, "sleet.json");
            await File.WriteAllTextAsync(config, Settings(FeedBase()).ToJsonString(), cancellationToken).ConfigureAwait(false);
            var result = await processes.RunAsync("sleet", [.. arguments, "--config", config, "--source", FeedName], folder.FullName, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
                throw new ToolException($"sleet {arguments[0]} failed: {result.Error.Trim()}{result.Output.Trim()}");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private Uri FeedBase()
    {
        var index = Require("HUB_FEED_URL");
        const string suffix = "index.json";
        return new Uri(index.EndsWith(suffix, StringComparison.Ordinal) ? index[..^suffix.Length] : index.TrimEnd('/') + "/");
    }

    private JsonObject Settings(Uri feedBase) => new()
    {
        ["sources"] = new JsonArray(new JsonObject
        {
            ["name"] = FeedName,
            ["type"] = "s3",
            ["serviceURL"] = $"https://{Require("HUB_FEED_ACCOUNT_ID")}.r2.cloudflarestorage.com",
            ["bucketName"] = Require("HUB_FEED_BUCKET"),
            ["path"] = feedBase.ToString(),
            ["baseURI"] = feedBase.ToString(),
            ["disablePayloadSigning"] = true,
            ["accessKeyId"] = Require("HUB_FEED_ACCESS_KEY_ID"),
            ["secretAccessKey"] = Require("HUB_FEED_SECRET_ACCESS_KEY"),
        }),
    };

    private string Require(string name) =>
        environment(name) is { Length: > 0 } value ? value : throw new ToolException($"The environment variable {name} is not set.");
}
