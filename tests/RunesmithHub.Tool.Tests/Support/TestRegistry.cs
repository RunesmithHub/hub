using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.Processes;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>A registry repository in a temporary folder, optionally under Git, for tests.</summary>
internal sealed class TestRegistry : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public TestRegistry()
    {
        File.WriteAllText(Combine(RegistryPaths.Policy), "{ \"oldestRunesmith\": null, \"mirrors\": [], \"holdHours\": 24 }\n");
        File.WriteAllText(Combine(RegistryPaths.ReservedNames), "# Reserved\nrunesmith\nofficial\nhub\n");
    }

    public string Path => _folder.Path;

    public IGitRepository Git => new GitCli(new SystemProcessRunner(), Path);

    public RegistryReader Reader => new(new DirectoryRegistryFiles(Path));

    public Task<RegistryState> LoadAsync() => Reader.LoadAsync(TestContext.Current.CancellationToken);

    public TestRegistry Publisher(PublisherFile publisher) => Write(RegistryPaths.Publisher(publisher.Name), RegistryJson.Serialize(publisher));

    public TestRegistry Registration(Protocol.Registry.Registration registration) => Write(RegistryPaths.Registration(registration.Id), RegistryJson.Serialize(registration));

    public TestRegistry Version(string pluginId, RegistryVersionRecord record) => Write(RegistryPaths.Version(pluginId, record.Version), RegistryJson.Serialize(record));

    public TestRegistry Moderation(RegistryModerationEntry entry) => Write(RegistryPaths.ModerationEntry(entry.Id), RegistryJson.Serialize(entry));

    public TestRegistry Root(int version, byte[] envelope) => Write($"{RegistryPaths.Roots}/{version}.json", envelope);

    public TestRegistry Write(string relativePath, byte[] bytes)
    {
        var path = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return this;
    }

    /// <summary>Starts a Git repository on the branch main.</summary>
    public void InitGit()
    {
        Run("init", "-q", "-b", "main");
        Run("config", "user.email", "tests@example.invalid");
        Run("config", "user.name", "Tests");
        Run("config", "commit.gpgsign", "false");
    }

    /// <summary>Commits everything and returns the commit's hash.</summary>
    public string Commit(string message)
    {
        Run("add", "-A");
        Run("commit", "-q", "--allow-empty", "-m", message);
        return Run("rev-parse", "HEAD").Trim();
    }

    public string Run(params string[] arguments)
    {
        var result = new SystemProcessRunner().RunAsync("git", arguments, Path, CancellationToken.None).GetAwaiter().GetResult();
        return result.Succeeded ? result.Output : throw new InvalidOperationException($"git {string.Join(' ', arguments)}: {result.Error}");
    }

    private string Combine(string relativePath)
    {
        var path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        return path;
    }

    public void Dispose() => _folder.Dispose();
}
