using RunesmithHub.Tool.Audit;
using RunesmithHub.Tool.Publishing;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>Artifacts served from local folders, by run ID and name.</summary>
internal sealed class FakeArtifactSource : IArtifactSource
{
    public Dictionary<(long Run, string Name), string> Folders { get; } = [];

    public Task DownloadAsync(long runId, string name, string directory, CancellationToken cancellationToken)
    {
        if (!Folders.TryGetValue((runId, name), out var source))
            throw new ToolException($"Run {runId} has no artifact named {name}.");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(directory, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return Task.CompletedTask;
    }
}

/// <summary>A hub feed that remembers what was pushed.</summary>
internal sealed class FakeFeed : IFeedPublisher
{
    public Dictionary<string, string> Packages { get; } = new(StringComparer.Ordinal);

    public Task<FeedPushResult> PushAsync(string packageFile, string packageId, string version, string sha256, CancellationToken cancellationToken)
    {
        var key = $"{packageId}@{version}";
        if (Packages.TryGetValue(key, out var existing))
            return existing == sha256 ? Task.FromResult(FeedPushResult.AlreadyThere) : throw new ToolException("different content");
        Packages[key] = sha256;
        return Task.FromResult(FeedPushResult.Pushed);
    }

    public Task<bool> DeleteAsync(string packageId, string version, CancellationToken cancellationToken) => Task.FromResult(Packages.Remove($"{packageId}@{version}"));
}

/// <summary>An advisory database with fixed answers.</summary>
internal sealed class FakeAdvisories : IAdvisoryDatabase
{
    public Dictionary<PackageVersion, IReadOnlyList<Advisory>> Known { get; } = [];

    public Task<IReadOnlyDictionary<PackageVersion, IReadOnlyList<Advisory>>> FindAsync(IReadOnlyCollection<PackageVersion> packages, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<PackageVersion, IReadOnlyList<Advisory>>>(packages.Where(Known.ContainsKey).ToDictionary(package => package, package => Known[package]));
}

/// <summary>Downloads served from memory by URL.</summary>
internal sealed class FakeDownloader : IDownloader
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public Task<byte[]?> GetAsync(Uri url, long maxBytes, CancellationToken cancellationToken) => Task.FromResult(Files.GetValueOrDefault(url.ToString()));
}
