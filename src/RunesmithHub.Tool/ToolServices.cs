using RunesmithHub.Protocol.Updating;
using RunesmithHub.Tool.Audit;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Processes;
using RunesmithHub.Tool.Publishing;

namespace RunesmithHub.Tool;

/// <summary>The outside services commands use, created on first use so commands that need none of them need no token.</summary>
internal sealed class ToolServices
{
    /// <summary>Gets what runs <c>git</c>, <c>dotnet</c> and <c>sleet</c>.</summary>
    public required IProcessRunner Processes { get; init; }

    /// <summary>Gets the client for package downloads and the vulnerability database.</summary>
    public required Func<HttpClient> Http { get; init; }

    public required Func<IGitHubReader> GitHub { get; init; }

    /// <summary>Gets the hub bot, acting with the installation token the workflow minted.</summary>
    public required Func<IGitHubWriter> Bot { get; init; }

    /// <summary>Gets a fetcher for an index location: an HTTPS base URL or a local folder.</summary>
    public required Func<string, IIndexFetcher> Index { get; init; }

    public required Func<IArtifactSource> Artifacts { get; init; }

    public required Func<IFeedPublisher> Feed { get; init; }

    public required Func<IAdvisoryDatabase> Advisories { get; init; }

    public required Func<IDownloader> Downloads { get; init; }

    /// <summary>Gets a Git repository by its folder.</summary>
    public required Func<string, IGitRepository> Git { get; init; }

    /// <summary>Gets an action that saves the conditional-request cache, when one is used.</summary>
    public Action SaveCache { get; init; } = () => { };

    /// <summary>Creates the real services from the environment: <c>GITHUB_TOKEN</c> and, for discovery, <c>HUB_GITHUB_CACHE</c>.</summary>
    public static ToolServices Create(Func<string, string?> environment, TimeProvider time)
    {
        var http = new Lazy<HttpClient>(CreateHttp);
        var cache = new Lazy<IResponseCache>(() => environment("HUB_GITHUB_CACHE") is { Length: > 0 } path ? new FileResponseCache(path) : NoResponseCache.Instance);
        var client = new Lazy<GitHubRestClient>(() => new GitHubRestClient(http.Value, environment("GITHUB_TOKEN"), cache.Value, time));
        var processes = new SystemProcessRunner();
        return new ToolServices
        {
            Processes = processes,
            Http = () => http.Value,
            GitHub = () => new GitHubReader(client.Value),
            Bot = () => new GitHubWriter(client.Value),
            Index = location => IndexLocation.CreateFetcher(location, http.Value),
            Artifacts = () => new GitHubArtifactSource(client.Value),
            Feed = () => new SleetFeedPublisher(processes, http.Value, environment),
            Advisories = () => new OsvAdvisoryDatabase(http.Value),
            Downloads = () => new HttpDownloader(http.Value),
            Git = directory => new GitCli(processes, directory),
            SaveCache = () => (cache.IsValueCreated ? cache.Value as FileResponseCache : null)?.Save(),
        };
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("rshub/" + typeof(ToolServices).Assembly.GetName().Version?.ToString(3));
        return http;
    }
}
