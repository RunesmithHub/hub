using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Build;

public sealed class CheckoutPathsTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public CheckoutPathsTests()
    {
        Directory.CreateDirectory(_folder.Combine("root", "bin"));
        File.WriteAllText(_folder.Combine("root", "bin", "a.dll"), "assembly");
        File.WriteAllText(_folder.Combine("outside.txt"), "outside");
    }

    private string Root => _folder.Combine("root");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void ReadsARegularFileInsideTheRoot() =>
        Assert.Equal("assembly"u8.ToArray(), CheckoutPaths.ReadFile(Root, _folder.Combine("root", "bin", "a.dll")));

    [Fact]
    public void RefusesAFileOutsideTheRoot()
    {
        var exception = Assert.Throws<ToolException>(() => CheckoutPaths.ReadFile(Root, _folder.Combine("root", "..", "outside.txt")));

        Assert.Contains("is outside", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAFileThatIsASymbolicLink()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links needs extra rights on Windows.");
        File.CreateSymbolicLink(_folder.Combine("root", "bin", "b.dll"), _folder.Combine("outside.txt"));

        var exception = Assert.Throws<ToolException>(() => CheckoutPaths.ReadFile(Root, _folder.Combine("root", "bin", "b.dll")));

        Assert.Contains("bin/b.dll is a symbolic link", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAFileInAFolderThatIsASymbolicLink()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links needs extra rights on Windows.");
        Directory.CreateSymbolicLink(_folder.Combine("root", "obj"), _folder.Path);

        var exception = Assert.Throws<ToolException>(() => CheckoutPaths.FileExists(Root, _folder.Combine("root", "obj", "outside.txt")));

        Assert.Contains("obj is a symbolic link", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TreatsAMissingFileAsMissing() =>
        Assert.False(CheckoutPaths.FileExists(Root, _folder.Combine("root", "bin", "missing.dll")));
}
