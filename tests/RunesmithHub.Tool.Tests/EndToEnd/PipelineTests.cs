using System.Text.Json;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Updating;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Json;
using RunesmithHub.Tool.Processes;
using RunesmithHub.Tool.Publishing;
using RunesmithHub.Tool.Tests.Support;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Tests.EndToEnd;

/// <summary>
/// Runs a release of a sample plugin through the whole pipeline the way the registry's workflows do, with real builds, real signing and
/// fake GitHub, artifact storage and feed: build, analysis, proposal, gates, publishing, a block, and what a client then sees.
/// </summary>
public sealed class PipelineTests : IDisposable
{
    private const long RunId = 18_000_001;
    private const int TrackingIssue = 501;
    private const long Administrator = 9001;
    private static readonly SemanticVersion Version = SemanticVersion.Parse(SamplePlugin.Version);

    private readonly TemporaryFolder _folder = new();
    private readonly TestRegistry _registry = new();
    private readonly ToolRunner _tool = new();
    private readonly SystemProcessRunner _processes = new();
    private readonly TestKeys _keys;

    public PipelineTests() => _keys = new TestKeys(_tool.Clock.GetUtcNow());

    public void Dispose()
    {
        _keys.Dispose();
        _registry.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task TakesAReleaseFromItsTagToPublishedAndThenBlocked()
    {
        Assert.SkipUnless(await IsOnPathAsync("dotnet") && await IsOnPathAsync("git"), "dotnet and git must be on the PATH to build the sample plugin.");
        var (repository, commit) = await WriteReleaseAsync();
        var registration = SetUpRegistry();
        var index = _folder.Combine("index");
        Directory.CreateDirectory(index);

        var plan = JsonDocument.Parse(await RunAsync("build", "prepare", "--source", repository, "--registration", registration, "--tag", "v1.0.0", "--commit", commit,
            "--output", _folder.Combine("prepared"))).RootElement;
        var implementation = plan.GetProperty("projects").GetProperty("implementation").GetString()!;
        var properties = "@" + plan.GetProperty("properties").GetString();
        await DotnetAsync(repository, "restore", implementation, properties);
        await DotnetAsync(repository, "build", implementation, "--no-restore", properties);

        var first = _folder.Combine("package");
        var second = _folder.Combine("rebuild");
        foreach (var output in new[] { first, second })
        {
            await RunAsync("build", "package", "--source", repository, "--output", output, "--commit", commit, "--run", HubLocations.RunUrl(RunId),
                "--registration", registration, "--sdk", plan.GetProperty("dotnetSdk").GetString()!);
        }

        Assert.Contains("identical", await RunAsync("build", "compare", Path.Combine(first, "hashes.json"), Path.Combine(second, "hashes.json")), StringComparison.Ordinal);

        var job = Job(commit);
        var analysis = _folder.Combine("analysis");
        Directory.CreateDirectory(analysis);
        await File.WriteAllBytesAsync(Path.Combine(analysis, BuildJob.FileName), ToolJson.Serialize(job), TestContext.Current.CancellationToken);
        Assert.Contains("no earlier published version", await RunAsync("version", "previous", "--build", Path.Combine(analysis, BuildJob.FileName),
            "--registry", _registry.Path, "--current", index, "--out", _folder.Combine("previous")), StringComparison.Ordinal);
        await RunAsync("analyse", "--package", Path.Combine(first, "lumen.sample-1.0.0.rsplugin"), "--registration", registration, "--source", repository, "--commit", commit,
            "--output", analysis);

        var pullRequest = await ProposeAndPassTheGatesAsync(analysis, first);
        Merge(pullRequest);

        var publications = _folder.Combine("publications.json");
        await RunAsync("publish", "artifacts", "--registry", _registry.Path, "--index", index, "--work", _folder.Combine("work"), "--out", publications);
        _keys.Use(_tool.Environment, OnlineRole.Publish, OnlineRole.Moderation, OnlineRole.Timestamp);
        Assert.Contains("bootstrapping", await RunAsync("index", "publish", "--registry", _registry.Path, "--index", index, "--publications", publications), StringComparison.Ordinal);

        var client = new InMemoryTrustStore();
        var published = await UpdateClientAsync(index, client);
        var record = published.Plugins[SamplePlugin.Id].FindVersion(Version)!;
        Assert.Equal(VersionState.Published, new HubCatalog(published).GetEffectiveState(SamplePlugin.Id, Version)!.State);
        Assert.Equal((ReviewState.Reviewed, (long?)Administrator), (record.Review.State, record.Review.By));
        Assert.Equal(["credentials", "network"], record.Capabilities.Detected);
        Assert.Equal("RunesmithHub.Lumen.Sample.Contracts", record.Contracts.Package);
        Assert.True(_tool.Feed.Packages.ContainsKey("RunesmithHub.Lumen.Sample.Contracts@1.0.0"));

        var downloaded = _tool.GitHub.DownloadAsset("lumen.sample@1.0.0", "lumen.sample-1.0.0.rsplugin")!;
        Assert.Equal((record.Package!.Sha256, record.Package.Length), (Hashes.Sha256(downloaded), (long)downloaded.Length));
        using (var archive = PackageArchive.Open(new MemoryStream(downloaded)))
            Assert.Equal((SamplePlugin.Id, Version), (archive.ReadManifest().Id, archive.ReadManifest().Version));

        await BlockAsync(index);

        var blocked = await UpdateClientAsync(index, client);
        Assert.Equal(VersionState.Blocked, new HubCatalog(blocked).GetEffectiveState(SamplePlugin.Id, Version)!.State);
        Assert.Equal(published.Snapshot.Version, blocked.Snapshot.Version);
        Assert.Equal(published.Moderation.Version + 1, blocked.Moderation.Version);
        Assert.Null(_tool.GitHub.DownloadAsset("lumen.sample@1.0.0", "lumen.sample-1.0.0.rsplugin"));
        Assert.NotNull(_tool.GitHub.DownloadAsset("lumen.sample@1.0.0", "lumen.sample-1.0.0.source.tar.gz"));
        Assert.True(_tool.Feed.Packages.ContainsKey("RunesmithHub.Lumen.Sample.Contracts@1.0.0"));
        Assert.Equal(VersionState.Blocked, new HubCatalog(await UpdateClientAsync(index, new InMemoryTrustStore())).GetEffectiveState(SamplePlugin.Id, Version)!.State);
    }

    private async Task<(string Repository, string Commit)> WriteReleaseAsync()
    {
        var sdk = _folder.Combine("sdk");
        SamplePlugin.WriteSdkStub(sdk);
        await ProcessAsync(sdk, "dotnet", "build", "-c", "Release", "-o", Path.Combine(sdk, "out"));
        var repository = _folder.Combine("repo");
        SamplePlugin.WritePlugin(repository, Path.Combine(sdk, "out", "Runesmith.Sdk.dll"), declareNetwork: true);
        await File.WriteAllTextAsync(Path.Combine(repository, ".gitignore"), "bin/\nobj/\n", TestContext.Current.CancellationToken);
        await ProcessAsync(repository, "git", "init", "--quiet");
        await ProcessAsync(repository, "dotnet", "restore", Path.Combine("Lumen.Sample", "Lumen.Sample.csproj"));
        await ProcessAsync(repository, "git", "add", "--all");
        await ProcessAsync(repository, "git", "-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", "Release");
        return (repository, (await _processes.RunAsync("git", ["rev-parse", "HEAD"], repository)).Output.Trim());
    }

    private string SetUpRegistry()
    {
        _registry.Root(1, _keys.Root)
            .Publisher(Samples.Publisher())
            .Registration(TestRegistrations.For(SamplePlugin.Id, "https://github.com/lumen/sample"));
        _registry.InitGit();
        _registry.Commit("Register lumen.sample");
        _tool.GitHub.Issues.Add(new Issue(TrackingIssue, "lumen.sample 1.0.0", "", true, [HubLabels.Tracking], FakeGitHub.Bot, Samples.Time, null, ""));
        _tool.GitHub.Teams["admins"].Add(Administrator);
        return Path.Combine(_registry.Path, "plugins", SamplePlugin.Id, "registration.json");
    }

    private static BuildJob Job(string commit) => new()
    {
        Plugin = SamplePlugin.Id,
        Version = SamplePlugin.Version,
        Tag = "v" + SamplePlugin.Version,
        Commit = commit,
        Repository = "lumen/sample",
        RepositoryId = 1,
        Path = ".",
        ReleaseAuthor = new GitHubAccount { Id = 2, Login = "lumen" },
        TrackingIssue = TrackingIssue,
        ReleaseNotes = "The first release.",
    };

    /// <summary>Proposes the version, applies its pull request on a branch of the registry and runs the three gates with an administrator's approval.</summary>
    private async Task<PullRequest> ProposeAndPassTheGatesAsync(string analysis, string package)
    {
        await RunAsync("version", "propose", "--analysis", analysis, "--build", Path.Combine(analysis, BuildJob.FileName), "--package", package,
            "--run-id", RunId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--registry", _registry.Path, "--current", _folder.Combine("index"));
        var (number, changes) = Assert.Single(_tool.GitHub.OpenedPullRequests);
        var proposed = RegistryJson.Deserialize<RegistryVersionRecord>(changes.Files.Single().Value!);
        Assert.Equal(VersionState.Review, proposed.State);
        Assert.Equal([ReviewTriggers.FirstVersion], proposed.Analysis.Triggers);

        _registry.Run("checkout", "-q", "-b", changes.Branch);
        _registry.Write(changes.Files.Single().Key, changes.Files.Single().Value!);
        var head = _registry.Commit(changes.Title);
        _registry.Run("checkout", "-q", "main");
        var pull = new PullRequest(number, changes.Title, changes.Body, changes.Branch, head, FakeGitHub.Bot, changes.Labels, true, false, Samples.Time, "");
        _tool.GitHub.PullRequests.Add(pull);
        _tool.Artifacts.Folders[(RunId, WorkflowArtifacts.Package)] = package;
        _tool.Artifacts.Folders[(RunId, WorkflowArtifacts.Analysis)] = analysis;

        string[] Gate(string name) => ["gate", name, "--pr", number.ToString(System.Globalization.CultureInfo.InvariantCulture), "--registry", _registry.Path, "--default-branch", "main"];
        await RunAsync(Gate("verify"));
        await RunAsync(Gate("review"));
        Assert.Equal(CommitStatusState.Pending, _tool.GitHub.Statuses.Last(status => status.Context == "hub/review").State);
        _tool.GitHub.Reviews[number] = [new PullRequestReview(new GitHubAccount { Id = Administrator, Login = "mod" }, "APPROVED", head, Samples.Time)];
        await RunAsync(Gate("review"));
        await RunAsync(Gate("hold"));
        Assert.All(_tool.GitHub.Statuses.GroupBy(status => status.Context), statuses => Assert.Equal(CommitStatusState.Success, statuses.Last().State));
        return pull;
    }

    private void Merge(PullRequest pull)
    {
        _registry.Run("merge", "-q", "--ff-only", pull.HeadRef);
        _tool.GitHub.PullRequests[_tool.GitHub.PullRequests.IndexOf(pull)] = pull with { Open = false, Merged = true };
    }

    /// <summary>Blocks the version as the moderate workflow does: sign the moderation list, withdraw the release files, sign the timestamp.</summary>
    private async Task BlockAsync(string index)
    {
        _registry.Moderation(new RegistryModerationEntry
        {
            Id = 1,
            Target = new ModerationTarget { Kind = ModerationTargetKind.Versions, Plugin = SamplePlugin.Id, Range = VersionRange.Parse("1.0.0") },
            Action = ModerationAction.Block,
            Reason = ModerationReason.Malicious,
            Note = "Sent tokens to a webhook.",
            CreatedAt = Samples.Time,
            CreatedBy = Administrator,
        });
        _registry.Commit("Block lumen.sample 1.0.0");
        _tool.Clock.Advance(TimeSpan.FromHours(1));

        _keys.Use(_tool.Environment, OnlineRole.Moderation);
        await RunAsync("index", "moderate", "--registry", _registry.Path, "--index", index);
        await RunAsync("publish", "withdraw", "--registry", _registry.Path);
        _keys.Use(_tool.Environment, OnlineRole.Timestamp);
        await RunAsync("index", "refresh", "--registry", _registry.Path, "--index", index);
    }

    private async Task<IndexView> UpdateClientAsync(string index, ITrustStore store)
    {
        var result = await new IndexUpdater(new DirectoryIndexFetcher(index), store, _keys.Root, _tool.Clock).UpdateAsync(TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Failure?.Message);
        return result.View;
    }

    private async Task<string> RunAsync(params string[] args)
    {
        var (exitCode, output, error) = await _tool.RunAsync(args);
        Assert.True(exitCode == 0, $"rshub {string.Join(' ', args.Take(2))} failed:\n{output}\n{error}");
        return output;
    }

    private Task DotnetAsync(string directory, params string[] arguments) => ProcessAsync(directory, "dotnet", arguments);

    private async Task ProcessAsync(string directory, string program, params string[] arguments)
    {
        var result = await _processes.RunAsync(program, arguments, directory, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, $"{program} {string.Join(' ', arguments)} failed:\n{result.Output}\n{result.Error}");
    }

    private async Task<bool> IsOnPathAsync(string program)
    {
        try
        {
            return (await _processes.RunAsync(program, ["--version"], _folder.Path, TestContext.Current.CancellationToken)).Succeeded;
        }
        catch (ToolException)
        {
            return false;
        }
    }
}
