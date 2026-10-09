using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Publishing;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Publishing;

public sealed class BlockedVersionRemoverTests : IDisposable
{
    private readonly TestRegistry _registry = new();
    private readonly TemporaryFolder _folder = new();
    private readonly FakeGitHub _github = new();
    private readonly FakeFeed _feed = new();

    public BlockedVersionRemoverTests() => _registry.Publisher(Samples.Publisher()).Registration(Samples.Registration());

    public void Dispose()
    {
        _folder.Dispose();
        _registry.Dispose();
    }

    /// <summary>Adds a published version with its release files and contracts package, as the publish job leaves them.</summary>
    private async Task ReleaseAsync(string version)
    {
        var record = await Samples.RecordAsync(await _registry.LoadAsync(), Samples.WriteBuildOutput(_folder.Combine(version), version), version);
        _registry.Version(Samples.PluginId, record);
        var release = await _github.EnsureReleaseAsync(HubLocations.Packages, $"lumen.todo@{version}", version, "", TestContext.Current.CancellationToken);
        foreach (var name in new[] { $"lumen.todo-{version}.rsplugin", $"lumen.todo-{version}.source.tar.gz", $"lumen.todo-{version}.cdx.json", "icon-64.png" })
            await _github.UploadAssetAsync(_github.PackageReleases[$"lumen.todo@{version}"], name, [1, 2, 3], "application/octet-stream", TestContext.Current.CancellationToken);
        _feed.Packages[$"RunesmithHub.Lumen.Todo.Contracts@{version}"] = record.Assets!.ContractsPackage.Sha256;
        Assert.NotNull(release);
    }

    private void Moderate(ModerationAction action, ModerationReason reason, string range) => _registry.Moderation(new RegistryModerationEntry
    {
        Id = 1,
        Target = new ModerationTarget { Kind = ModerationTargetKind.Versions, Plugin = Samples.PluginId, Range = VersionRange.Parse(range) },
        Action = action,
        Reason = reason,
        Note = "Note.",
        CreatedAt = Samples.Time,
        CreatedBy = 9001,
    });

    private async Task<IReadOnlyList<string>> RemoveAsync() =>
        await new BlockedVersionRemover(_github, _github, _feed).RemoveAsync(await _registry.LoadAsync(), TestContext.Current.CancellationToken);

    private IEnumerable<string> Assets(string version) => _github.PackageReleases[$"lumen.todo@{version}"].Assets.Select(asset => asset.Name);

    [Fact]
    public async Task DeletesTheReleaseFilesOfABlockedVersionButKeepsItsSourceArchive()
    {
        await ReleaseAsync("1.0.0");
        await ReleaseAsync("1.1.0");
        Moderate(ModerationAction.Block, ModerationReason.Malicious, "1.0.0");

        var notes = await RemoveAsync();

        Assert.Equal(["lumen.todo-1.0.0.source.tar.gz"], Assets("1.0.0"));
        Assert.Equal(4, Assets("1.1.0").Count());
        Assert.True(_feed.Packages.ContainsKey("RunesmithHub.Lumen.Todo.Contracts@1.0.0"));
        Assert.Equal(["lumen.todo 1.0.0: removed 3 release files."], notes);
        Assert.Equal(["Nothing to remove."], await RemoveAsync());
    }

    [Fact]
    public async Task DeletesEverythingIncludingTheContractsPackageForALegalBlock()
    {
        await ReleaseAsync("1.0.0");
        Moderate(ModerationAction.Block, ModerationReason.Legal, ">=1.0.0");

        await RemoveAsync();

        Assert.Empty(Assets("1.0.0"));
        Assert.False(_feed.Packages.ContainsKey("RunesmithHub.Lumen.Todo.Contracts@1.0.0"));
    }

    [Fact]
    public async Task LeavesFrozenVersionsAlone()
    {
        await ReleaseAsync("1.0.0");
        Moderate(ModerationAction.Freeze, ModerationReason.Legal, "1.0.0");

        Assert.Equal(["Nothing to remove."], await RemoveAsync());
        Assert.Equal(4, Assets("1.0.0").Count());
    }

    [Fact]
    public async Task TreatsEveryVersionOfABlockedPluginAsBlocked()
    {
        await ReleaseAsync("1.0.0");
        _registry.Registration(Samples.Registration(state: RegistrationState.Blocked));

        await RemoveAsync();

        Assert.Equal(["lumen.todo-1.0.0.source.tar.gz"], Assets("1.0.0"));
    }
}
