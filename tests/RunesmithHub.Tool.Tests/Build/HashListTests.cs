using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Build;

public sealed class HashListTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string Write(string build, params (string Path, string Content)[] files)
    {
        foreach (var (path, content) in files)
        {
            var file = _folder.Combine(build, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        var list = HashList.Create(_folder.Combine(build));
        list.Write(_folder.Combine(build));
        return _folder.Combine(build, HashList.FileName);
    }

    [Fact]
    public void HashesEveryFileButItselfSortedByPath()
    {
        var path = Write("a", ("b.txt", "b"), ("images/a.png", "a"));

        var list = HashList.Read(path);

        Assert.Equal(["b.txt", "images/a.png"], list.Files.Select(file => file.Path));
        Assert.Equal("3e23e8160039594a33894f6564e1b1348bbd7a0088d42c4acb73eeaed59c009d", list.Files[0].Sha256);
        Assert.Equal(1, list.Files[0].Length);
    }

    [Fact]
    public async Task CompareExitsWithZeroForIdenticalBuilds()
    {
        var first = Write("a", ("x.txt", "same"));
        var second = Write("b", ("x.txt", "same"));

        var (exitCode, output, _) = await new ToolRunner().RunAsync("build", "compare", first, second);

        Assert.Equal(0, exitCode);
        Assert.Contains("identical", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompareListsEveryFileThatDiffersOrIsMissing()
    {
        var first = Write("a", ("same.txt", "same"), ("changed.txt", "one"), ("only-a.txt", "a"));
        var second = Write("b", ("same.txt", "same"), ("changed.txt", "two"), ("only-b.txt", "b"));

        var (exitCode, output, _) = await new ToolRunner().RunAsync("build", "compare", first, second);

        Assert.Equal(1, exitCode);
        Assert.Contains("  changed.txt\n  only-a.txt\n  only-b.txt\n", output.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("same.txt", output, StringComparison.Ordinal);
    }
}
