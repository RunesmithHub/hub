using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>A package source for builds that bundle no NuGet packages; asking it for one fails the test.</summary>
internal sealed class NoPackages : INuGetPackageSource
{
    public Task<byte[]> GetAsync(string id, string version, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException($"The test did not expect {id} {version} to be fetched.");
}
