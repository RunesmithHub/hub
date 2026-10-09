using RunesmithHub.Protocol.Index;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.NuGet;
using RunesmithHub.Tool.Processes;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Build;

public sealed class BuildPreparerTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Repository = "https://github.com/lumen/sample";

    private readonly TemporaryFolder _folder = new();
    private readonly FakeProcesses _git = new FakeProcesses()
        .Answer("git rev-parse HEAD", Commit + "\n")
        .Answer($"git ls-tree -r {Commit}", "100644 blob 1111111111111111111111111111111111111111\tplugin.json\n");

    public BuildPreparerTests()
    {
        SamplePlugin.WritePlugin(Root, "/sdk/Runesmith.Sdk.dll", declareNetwork: true);
        WriteLockFile("Lumen.Sample.Contracts", "0.1.0");
        WriteLockFile("Lumen.Sample", "0.1.0");
    }

    private string Root => _folder.Combine("repo");

    public void Dispose() => _folder.Dispose();

    private void WriteLockFile(string project, string sdkVersion) => File.WriteAllText(Path.Combine(Root, project, LockFile.FileName), $$"""
        {
          "version": 1,
          "dependencies": {
            "net10.0": {
              "Runesmith.Sdk": { "type": "Direct", "requested": "[{{sdkVersion}}, )", "resolved": "{{sdkVersion}}", "contentHash": "AAAA" }
            }
          }
        }
        """);

    private Task<BuildPlan> PrepareAsync(string tag = "v1.0.0", string commit = Commit)
    {
        var expectations = PluginExpectations.ForRelease(TestRegistrations.For(SamplePlugin.Id, Repository, PluginTier.Unverified), tag);
        var request = new PrepareRequest(Root, ".", expectations, commit, _folder.Combine("build"), new Uri(NuGetFeeds.DefaultHubFeed));
        return new BuildPreparer(new GitCheckout(_git)).PrepareAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WritesTheHubConfigurationAndReturnsThePlan()
    {
        var plan = await PrepareAsync();

        var config = await File.ReadAllTextAsync(plan.NuGetConfig, TestContext.Current.CancellationToken);
        var properties = await File.ReadAllTextAsync(plan.Properties, TestContext.Current.CancellationToken);
        Assert.Equal(SamplePlugin.Id, plan.Id);
        Assert.Equal("1.0.0", plan.Version);
        Assert.Equal("10.0.400", plan.DotnetSdk);
        Assert.Equal("0.1.0", plan.RunesmithSdk);
        Assert.Equal(Path.Combine(Root, "Lumen.Sample", "Lumen.Sample.csproj"), plan.Projects.Implementation);
        Assert.Contains("<add key=\"runesmith-hub\" value=\"https://nuget.runesmith.dev/index.json\" />", config, StringComparison.Ordinal);
        Assert.Contains("<packageSource key=\"runesmith-hub\">\n      <package pattern=\"Runesmith.*\" />\n      <package pattern=\"RunesmithHub.*\" />", config, StringComparison.Ordinal);
        Assert.Contains("<packageSource key=\"nuget.org\">\n      <package pattern=\"*\" />", config, StringComparison.Ordinal);
        foreach (var expected in (string[])["\"-p:Deterministic=true\"", "\"-p:ContinuousIntegrationBuild=true\"", "\"-p:RestoreLockedMode=true\"", $"\"-p:PathMap={Root}=/_/\"",
            "\"-p:RunesmithSdkVersion=0.1.0\"", "\"-p:AssemblyVersion=1.0.0.0\"", "\"-p:FileVersion=1.0.0.0\"", $"\"-p:RestoreConfigFile={plan.NuGetConfig}\""])
            Assert.Contains(expected, properties, StringComparison.Ordinal);
        Assert.Equal(properties, BuildProperties.ResponseFile(plan.PropertyArguments));
    }

    [Fact]
    public async Task RejectsATagThatDoesNotNameTheManifestsVersion()
    {
        var exception = await Assert.ThrowsAsync<ToolException>(() => PrepareAsync(tag: "v1.0.1"));

        Assert.Contains("version: must be 1.0.1, the version of the release tag.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsATagWithoutThePrefix() =>
        Assert.Throws<ToolException>(() => PluginExpectations.ForRelease(TestRegistrations.For(SamplePlugin.Id, Repository), "1.0.0"));

    [Fact]
    public async Task RejectsACheckoutAtAnotherCommit()
    {
        var exception = await Assert.ThrowsAsync<ToolException>(() => PrepareAsync(commit: new string('f', 40)));

        Assert.Contains("not the release commit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsSubmodules()
    {
        _git.Answer($"git ls-tree -r {Commit}", "160000 commit 2222222222222222222222222222222222222222\tvendor/lib\n");

        var exception = await Assert.ThrowsAsync<ToolException>(() => PrepareAsync());

        Assert.Contains("Submodules are not supported", exception.Message, StringComparison.Ordinal);
        Assert.Contains("vendor/lib", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListsEveryMissingFile()
    {
        File.Delete(Path.Combine(Root, "LICENSE"));
        File.Delete(Path.Combine(Root, "Lumen.Sample", LockFile.FileName));

        var exception = await Assert.ThrowsAsync<ToolException>(() => PrepareAsync());

        Assert.Contains("LICENSE is missing.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Lumen.Sample/packages.lock.json is missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequiresTheSdkAtTheMinimumOfTheApiRange()
    {
        WriteLockFile("Lumen.Sample", "0.1.3");

        var exception = await Assert.ThrowsAsync<ToolException>(() => PrepareAsync());

        Assert.Contains("locks Runesmith.Sdk 0.1.3; it must reference 0.1.0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesPathsMsBuildPropertiesCannotHold() =>
        Assert.Throws<ToolException>(() => BuildProperties.Arguments("/tmp/a;b", "/tmp/nuget.config", Protocol.Versioning.SemanticVersion.Parse("1.0.0"), Protocol.Versioning.SemanticVersion.Parse("0.1.0")));
}
