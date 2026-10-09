using System.Text.Json;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Tool.NuGet;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Build;

public sealed class BuildCommandsTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task PreparePrintsThePlanAsJsonAndUsesTheFeedFromTheEnvironment()
    {
        var root = _folder.Combine("repo", "plugins", "sample");
        SamplePlugin.WritePlugin(root, "/sdk/Runesmith.Sdk.dll", declareNetwork: false);
        foreach (var project in (string[])["Lumen.Sample", "Lumen.Sample.Contracts"])
            File.WriteAllText(Path.Combine(root, project, LockFile.FileName), """{ "version": 1, "dependencies": { "net10.0": {} } }""");
        var registration = TestRegistrations.For(SamplePlugin.Id, "https://github.com/lumen/sample");
        registration = registration with { Repository = registration.Repository with { Path = "plugins/sample", TagPrefix = "release-" } };
        File.WriteAllBytes(_folder.Combine("registration.json"), RegistryJson.Serialize(registration));
        var tool = new ToolRunner { Processes = new FakeProcesses().Answer("git rev-parse HEAD", Commit).Answer($"git ls-tree -r {Commit}", "") };
        tool.Environment[NuGetFeeds.HubFeedVariable] = "https://feed.example/index.json";
        tool.Environment["GITHUB_OUTPUT"] = _folder.Combine("github-output");

        var (exitCode, output, error) = await tool.RunAsync(
            "build", "prepare", "--source", _folder.Combine("repo"), "--registration", _folder.Combine("registration.json"), "--tag", "release-1.0.0", "--commit", Commit, "--output", _folder.Combine("build"));

        Assert.True(exitCode == 0, error);
        using var plan = JsonDocument.Parse(output);
        Assert.Equal(root, plan.RootElement.GetProperty("pluginRoot").GetString());
        Assert.Equal("0.1.0", plan.RootElement.GetProperty("runesmithSdk").GetString());
        Assert.Contains("https://feed.example/index.json", await File.ReadAllTextAsync(_folder.Combine("build", NuGetConfigWriter.FileName), TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var outputs = await File.ReadAllLinesAsync(_folder.Combine("github-output"), TestContext.Current.CancellationToken);
        Assert.Equal(
            [$"plugin-root={root}", $"implementation={Path.Combine(root, "Lumen.Sample", "Lumen.Sample.csproj")}", $"properties={_folder.Combine("build", "hub-build.rsp")}", "dotnet-sdk=10.0.400"],
            outputs);
    }

    [Fact]
    public async Task PrepareReportsAnInvalidRegistration()
    {
        File.WriteAllText(_folder.Combine("registration.json"), """{ "id": "lumen.sample", "unknown": true }""");

        var (exitCode, _, error) = await new ToolRunner().RunAsync(
            "build", "prepare", "--source", _folder.Path, "--registration", _folder.Combine("registration.json"), "--tag", "v1.0.0", "--commit", Commit);

        Assert.Equal(1, exitCode);
        Assert.Contains("is not a valid registration", error, StringComparison.Ordinal);
    }
}
