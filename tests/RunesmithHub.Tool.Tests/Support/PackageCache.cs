using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>A package source with the packages a test gives it, which remembers what was asked for.</summary>
internal sealed class PackageCache(IReadOnlyDictionary<(string Id, string Version), byte[]> packages) : INuGetPackageSource
{
    public List<string> Requested { get; } = [];

    public Task<byte[]> GetAsync(string id, string version, CancellationToken cancellationToken = default)
    {
        Requested.Add($"{id} {version}");
        return packages.TryGetValue((id, version), out var bytes) ? Task.FromResult(bytes) : throw new ToolException($"{id} {version} is not on the test feed.");
    }
}
