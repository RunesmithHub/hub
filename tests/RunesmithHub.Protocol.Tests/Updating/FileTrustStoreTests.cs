using System.Text;
using RunesmithHub.Protocol.Tests.Support;
using RunesmithHub.Protocol.Updating;

namespace RunesmithHub.Protocol.Tests.Updating;

public sealed class FileTrustStoreTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static TrustedIndex Index(string marker) => new(Encoding.UTF8.GetBytes("root-" + marker), new TrustedFiles
    {
        Timestamp = "t"u8.ToArray(),
        Snapshot = "s"u8.ToArray(),
        Moderation = "m"u8.ToArray(),
        Publishers = "p"u8.ToArray(),
        Policy = "y"u8.ToArray(),
        Plugins = new Dictionary<string, ReadOnlyMemory<byte>> { ["lumen.todo"] = Encoding.UTF8.GetBytes(marker) },
    });

    [Fact]
    public void LoadsNothingBeforeTheFirstSave()
    {
        Assert.Null(new FileTrustStore(_folder.Path).Load());
    }

    [Fact]
    public void LoadsWhatWasSaved()
    {
        new FileTrustStore(_folder.Path).Save(Index("one"));

        var loaded = new FileTrustStore(_folder.Path).Load()!;

        Assert.Equal("root-one"u8.ToArray(), loaded.Root.ToArray());
        Assert.Equal("one"u8.ToArray(), loaded.Files!.Plugins["lumen.todo"].ToArray());
        Assert.Equal("s"u8.ToArray(), loaded.Files.Snapshot.ToArray());
    }

    [Fact]
    public void ReplacesEverythingOnSaveAndLeavesNoTemporaryFolders()
    {
        var store = new FileTrustStore(_folder.Path);
        store.Save(Index("one"));
        store.Save(new TrustedIndex("root-two"u8.ToArray(), null));

        var loaded = store.Load()!;

        Assert.Equal("root-two"u8.ToArray(), loaded.Root.ToArray());
        Assert.Null(loaded.Files);
        Assert.Equal(["current"], Directory.GetDirectories(_folder.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void RecoversThePreviousCopyAfterASaveStoppedPartWay()
    {
        var store = new FileTrustStore(_folder.Path);
        store.Save(Index("one"));
        Directory.Move(Path.Combine(_folder.Path, "current"), Path.Combine(_folder.Path, ".retired-1"));
        Directory.CreateDirectory(Path.Combine(_folder.Path, ".staging-2"));

        var loaded = store.Load()!;

        Assert.Equal("root-one"u8.ToArray(), loaded.Root.ToArray());
        store.Save(Index("two"));
        Assert.Equal(["current"], Directory.GetDirectories(_folder.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void RefusesPluginIdsThatAreNotSafeFileNames()
    {
        var index = Index("one");
        var bad = index with { Files = index.Files! with { Plugins = new Dictionary<string, ReadOnlyMemory<byte>> { ["../evil"] = new byte[1] } } };
        var store = new FileTrustStore(_folder.Path);
        store.Save(Index("one"));

        Assert.Throws<ArgumentException>(() => store.Save(bad));
        Assert.Equal("root-one"u8.ToArray(), store.Load()!.Root.ToArray());
    }
}
