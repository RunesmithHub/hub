using System.Formats.Tar;
using System.IO.Compression;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.Processes;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Git;

public sealed class GitProcessTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();
    private readonly SystemProcessRunner _processes = new();

    public void Dispose() => _folder.Dispose();

    private string Root => _folder.Combine("repo");

    private async Task<string> CommitAsync(string directory, params (string Path, string Content)[] files)
    {
        Directory.CreateDirectory(directory);
        foreach (var (path, content) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(directory, path))!);
            await File.WriteAllTextAsync(Path.Combine(directory, path), content, TestContext.Current.CancellationToken);
        }

        await RunAsync(directory, "init", "--quiet");
        await RunAsync(directory, "add", "--all");
        await RunAsync(directory, "-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", "Release");
        return (await RunAsync(directory, "rev-parse", "HEAD")).Trim();
    }

    private async Task<string> RunAsync(string directory, params string[] arguments) =>
        (await _processes.RunAsync("git", arguments, directory, TestContext.Current.CancellationToken)).OutputOrThrow("git");

    private async Task SkipUnlessGitAsync()
    {
        try
        {
            Assert.SkipUnless((await _processes.RunAsync("git", ["--version"], _folder.Path, TestContext.Current.CancellationToken)).Succeeded, "git must be on the PATH.");
        }
        catch (ToolException)
        {
            Assert.Skip("git must be on the PATH.");
        }
    }

    [Fact]
    public async Task ReadsOnlyTheRepositoryConfigurationAndTheHubSettings()
    {
        await SkipUnlessGitAsync();
        await CommitAsync(Root, ("plugin.json", "{}"));

        var result = await GitProcess.RunAsync(_processes, Root, ["config", "--list", "--show-scope"], TestContext.Current.CancellationToken);

        var scopes = result.OutputOrThrow("git config").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('\t')[0]).Distinct().Order(StringComparer.Ordinal);
        Assert.Equal(["command", "local"], scopes);
    }

    [Fact]
    public async Task UsesTheRepositoryAtTheRootRatherThanOneInsideTheWorkTree()
    {
        await SkipUnlessGitAsync();
        var outer = await CommitAsync(Root, ("plugin.json", "{}"));
        var inner = await CommitAsync(Path.Combine(Root, "plugin"), ("inner.txt", "inner"));

        var head = await new GitCheckout(_processes).HeadCommitAsync(Root, TestContext.Current.CancellationToken);

        Assert.NotEqual(inner, outer);
        Assert.Equal(outer, head);
    }

    [Fact]
    public async Task ArchivesTheCommittedTreeWhateverTheWorkTreeHolds()
    {
        await SkipUnlessGitAsync();
        var commit = await CommitAsync(Root, ("src/a.txt", "committed"));
        await File.WriteAllTextAsync(Path.Combine(Root, "src", "a.txt"), "changed", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Root, ".gitattributes"), "src/a.txt export-ignore\n", TestContext.Current.CancellationToken);

        var archive = await new SourceArchiver(new GitCheckout(_processes)).CreateAsync(Root, commit, "sample-1.0.0", TestContext.Current.CancellationToken);

        using var reader = new TarReader(new GZipStream(new MemoryStream(archive), CompressionMode.Decompress));
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.GetNextEntryAsync(copyData: true, TestContext.Current.CancellationToken) is { } entry)
        {
            if (entry.DataStream is { } data)
                entries[entry.Name] = await new StreamReader(data).ReadToEndAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal("committed", entries["sample-1.0.0/src/a.txt"]);
        Assert.DoesNotContain("sample-1.0.0/.gitattributes", entries.Keys);
    }

    [Fact]
    public void FindsTheNearestFolderWithAGitFolderOrFile()
    {
        Directory.CreateDirectory(_folder.Combine("repo", ".git"));
        Directory.CreateDirectory(_folder.Combine("repo", "plugins", "sample"));

        Assert.Equal(Root, GitProcess.FindRepositoryRoot(_folder.Combine("repo", "plugins", "sample")));
    }
}
