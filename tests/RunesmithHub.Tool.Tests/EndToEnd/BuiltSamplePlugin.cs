using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.NuGet;
using RunesmithHub.Tool.Processes;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.EndToEnd;

/// <summary>
/// The sample plugin in a Git repository, prepared and built in Release with <c>dotnet</c> the way the hub's build job does, once for every
/// test that uses it. <see cref="Available"/> is false when <c>dotnet</c> or <c>git</c> is not on the PATH.
/// </summary>
public sealed class BuiltSamplePlugin : IAsyncLifetime
{
    private readonly TemporaryFolder _folder = new();
    private readonly SystemProcessRunner _processes = new();

    public bool Available { get; private set; }

    internal string Repository => _folder.Combine("repo");

    internal string Commit { get; private set; } = "";

    /// <summary>Gets the source archive the preparation wrote.</summary>
    internal string SourceArchive { get; private set; } = "";

    internal string Folder(string name) => _folder.Combine(name);

    public async ValueTask InitializeAsync()
    {
        if (!await IsOnPathAsync("dotnet") || !await IsOnPathAsync("git"))
            return;

        var sdk = _folder.Combine("sdk");
        SamplePlugin.WriteSdkStub(sdk);
        await RunAsync(sdk, "dotnet", ["build", "-c", "Release", "-o", Path.Combine(sdk, "out")]);
        SamplePlugin.WritePlugin(Repository, Path.Combine(sdk, "out", "Runesmith.Sdk.dll"), declareNetwork: false);
        File.WriteAllText(Path.Combine(Repository, ".gitignore"), "bin/\nobj/\n");

        await RunAsync(Repository, "git", ["init", "--quiet"]);
        await RunAsync(Repository, "dotnet", ["restore", Path.Combine("Lumen.Sample", "Lumen.Sample.csproj")]);
        await RunAsync(Repository, "git", ["add", "--all"]);
        await RunAsync(Repository, "git", ["-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", "Release"]);
        Commit = (await _processes.RunAsync("git", ["rev-parse", "HEAD"], Repository)).Output.Trim();

        var request = new PrepareRequest(Repository, ".", new PluginExpectations(SamplePlugin.Id, "https://github.com/lumen/sample", SamplePlugin.Version), Commit, _folder.Combine("build"), new Uri(NuGetFeeds.DefaultHubFeed));
        var plan = await new BuildPreparer(new GitCheckout(_processes)).PrepareAsync(request);
        SourceArchive = plan.SourceArchive!;
        await RunAsync(Repository, "dotnet", ["restore", plan.Projects.Implementation, .. plan.PropertyArguments]);
        await RunAsync(Repository, "dotnet", ["build", plan.Projects.Implementation, "--no-restore", .. plan.PropertyArguments]);
        Available = true;
    }

    private async Task<bool> IsOnPathAsync(string program)
    {
        try
        {
            return (await _processes.RunAsync(program, ["--version"], _folder.Path)).Succeeded;
        }
        catch (ToolException)
        {
            return false;
        }
    }

    private async Task RunAsync(string directory, string program, IReadOnlyList<string> arguments)
    {
        var result = await _processes.RunAsync(program, arguments, directory);
        if (!result.Succeeded)
            throw new InvalidOperationException($"{program} {string.Join(' ', arguments)} failed:\n{result.Output}\n{result.Error}");
    }

    public ValueTask DisposeAsync()
    {
        _folder.Dispose();
        return ValueTask.CompletedTask;
    }
}
