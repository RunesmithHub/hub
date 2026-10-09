using RunesmithHub.Protocol.Index;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.IndexBuild;

/// <summary>An index directory as the signing jobs keep it on the index branch: read, rebuilt in place, and pruned.</summary>
public sealed class IndexDirectoryTests : IDisposable
{
    private readonly TestRegistry _registry = new();
    private readonly TemporaryFolder _folder = new();
    private readonly ToolRunner _tool = new();
    private readonly TestKeys _keys;

    public IndexDirectoryTests()
    {
        _keys = new TestKeys(_tool.Clock.GetUtcNow());
        _registry.Root(1, _keys.Root).Publisher(Samples.Publisher());
    }

    public void Dispose()
    {
        _keys.Dispose();
        _folder.Dispose();
        _registry.Dispose();
    }

    private string Index => _folder.Combine("index");

    private async Task RunAsync(string operation, params OnlineRole[] roles)
    {
        _keys.Use(_tool.Environment, roles);
        var (exitCode, _, error) = await _tool.RunAsync("index", operation, "--registry", _registry.Path, "--index", Index);
        Assert.True(exitCode == 0, error);
    }

    private TrustRoot Trust => TrustRoot.Verify([_keys.Root]);

    [Fact]
    public async Task KeepsWhatTheTimestampNamesUntilTheNextTimestampAndDropsOlderGenerations()
    {
        await RunAsync("publish", OnlineRole.Publish, OnlineRole.Moderation, OnlineRole.Timestamp);
        var first = IndexState.Read(Index, Trust)!;
        _registry.Publisher(Samples.Publisher() with { DisplayName = "Lumen" });
        await RunAsync("publish", OnlineRole.Publish);
        var pending = IndexState.Read(Index, Trust)!;

        Assert.Equal(2, pending.Snapshot!.Payload.Version);
        Assert.Equal(first.Timestamp!.Version, pending.Timestamp!.Version);
        Assert.True(File.Exists(Path.Combine(Index, first.Snapshot!.Path)));

        await RunAsync("timestamp", OnlineRole.Timestamp);
        await RunAsync("refresh", OnlineRole.Timestamp);

        Assert.False(File.Exists(Path.Combine(Index, first.Snapshot.Path)));
        Assert.True(File.Exists(Path.Combine(Index, pending.Snapshot.Path)));
        Assert.True(File.Exists(Path.Combine(Index, IndexPaths.Root(1))));
    }

    [Fact]
    public async Task LeavesFilesThatAreNotPartOfTheIndexAlone()
    {
        Directory.CreateDirectory(Index);
        await File.WriteAllTextAsync(Path.Combine(Index, "README.md"), "Not the index.", TestContext.Current.CancellationToken);

        await RunAsync("publish", OnlineRole.Publish, OnlineRole.Moderation, OnlineRole.Timestamp);

        Assert.True(File.Exists(Path.Combine(Index, "README.md")));
    }

    [Fact]
    public async Task RefusesATimestampTheTimestampKeyDidNotSign()
    {
        await RunAsync("publish", OnlineRole.Publish, OnlineRole.Moderation, OnlineRole.Timestamp);
        var timestamp = Path.Combine(Index, IndexPaths.Timestamp);
        var forged = (await File.ReadAllTextAsync(timestamp, TestContext.Current.CancellationToken)).Replace("\"sig\":\"", "\"sig\":\"AAAA", StringComparison.Ordinal);
        await File.WriteAllTextAsync(timestamp, forged, TestContext.Current.CancellationToken);

        var exception = Assert.Throws<ToolException>(() => IndexState.Read(Index, Trust));

        Assert.Contains("not signed by the timestamp key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAFileThatIsNotNamedByItsHash()
    {
        await RunAsync("publish", OnlineRole.Publish, OnlineRole.Moderation, OnlineRole.Timestamp);
        var policy = Directory.EnumerateFiles(Path.Combine(Index, "policy")).Single();
        await File.AppendAllTextAsync(policy, " ", TestContext.Current.CancellationToken);

        var exception = Assert.Throws<ToolException>(() => IndexState.Read(Index, Trust));

        Assert.Contains("is not named by its SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritesTheDashboardIntoTheIndexAndKeepsItThroughIndexBuilds()
    {
        var (exitCode, _, error) = await _tool.RunAsync("dashboard", "--registry", _registry.Path, "--index", Index);
        Assert.True(exitCode == 0, error);

        await RunAsync("publish", OnlineRole.Publish, OnlineRole.Moderation, OnlineRole.Timestamp);

        Assert.True(File.Exists(Path.Combine(Index, IndexPaths.Dashboard)));
        Assert.Contains(IndexPaths.Dashboard, IndexState.Read(Index, Trust)!.Files.Keys);
    }

    [Fact]
    public async Task RefusesToRefreshAnIndexWithoutATimestamp()
    {
        await RunAsync("publish", OnlineRole.Publish);
        _keys.Use(_tool.Environment, OnlineRole.Timestamp);

        var (exitCode, _, error) = await _tool.RunAsync("index", "refresh", "--registry", _registry.Path, "--index", Index);

        Assert.Equal(1, exitCode);
        Assert.Contains("no timestamp yet", error, StringComparison.Ordinal);
    }
}
