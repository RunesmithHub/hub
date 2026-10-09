using System.IO.Compression;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Tests.Support;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Tests.Versions;

public sealed class BuildOutputTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static BuildOutput Open(string directory) => BuildOutput.Open(directory, Samples.PluginId, SemanticVersion.Parse("1.0.0"), PackageLimits.Default);

    [Fact]
    public void FindsTheFilesTheBuildWrites()
    {
        var output = Open(Samples.WriteBuildOutput(_folder.Path, "1.0.0", banner: true));

        Assert.Equal("RunesmithHub.Lumen.Todo.Contracts", output.ContractsPackageId);
        Assert.Equal([64, 128, 256], output.Icons.Select(icon => icon.Width));
        Assert.Equal((640, 320), (output.Banner!.Value.Width640.Width, output.Banner.Value.Width640.Height));
        Assert.Equal(
            ["images/banner-1280.webp", "images/banner-640.webp", "images/icon-128.png", "images/icon-256.png", "images/icon-64.png", "lumen.todo-1.0.0.cdx.json",
                "lumen.todo-1.0.0.rsplugin", "lumen.todo-1.0.0.source.tar.gz"],
            output.ReleaseFiles.Select(file => file.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RefusesAContractsPackageWithAnotherAssembly()
    {
        var directory = Samples.WriteBuildOutput(_folder.Path, "1.0.0");
        using (var archive = ZipFile.Open(Path.Combine(directory, "RunesmithHub.Lumen.Todo.Contracts.1.0.0.nupkg"), ZipArchiveMode.Update))
        {
            archive.GetEntry("lib/net10.0/Lumen.Todo.Contracts.dll")!.Delete();
            using var stream = archive.CreateEntry("lib/net10.0/Lumen.Todo.Contracts.dll").Open();
            stream.Write("something else"u8);
        }

        var exception = Assert.Throws<ToolException>(() => Open(directory));

        Assert.Contains("is not the contracts assembly of the package", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnImageListThatMisstatesAnImage()
    {
        var directory = Samples.WriteBuildOutput(_folder.Path, "1.0.0");
        File.Copy(Path.Combine(directory, "images", "icon-128.png"), Path.Combine(directory, "images", "icon-64.png"), overwrite: true);

        var exception = Assert.Throws<ToolException>(() => Open(directory));

        Assert.Contains("icon-64.png is not the 64x64 image", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesTheOutputOfAnotherVersion()
    {
        var directory = Samples.WriteBuildOutput(_folder.Path, "1.0.0");
        File.Move(Path.Combine(directory, "lumen.todo-1.0.0.rsplugin"), Path.Combine(directory, "lumen.todo-1.1.0.rsplugin"));

        Assert.Throws<ToolException>(() => Open(directory));
    }
}
