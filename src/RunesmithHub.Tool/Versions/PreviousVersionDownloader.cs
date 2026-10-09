using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Build;

namespace RunesmithHub.Tool.Versions;

/// <summary>Downloads the package of the version a new version is analysed against: the highest lower version the index still hosts.</summary>
internal sealed class PreviousVersionDownloader(IDownloader downloads)
{
    /// <summary>Writes the previous version's package into a folder and returns its path, or returns null for a first version.</summary>
    /// <exception cref="ToolException">The package can't be downloaded or does not match the index.</exception>
    public async Task<string?> DownloadAsync(HubCatalog catalog, string pluginId, SemanticVersion version, string directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.FindPlugin(pluginId) is not { } plugin)
            return null;
        var previous = plugin.Versions
            .Where(record => record.Version < version && record.Package is not null
                && catalog.GetEffectiveState(plugin, record).State is not (VersionState.Blocked or VersionState.Deleted))
            .MaxBy(record => record.Version);
        if (previous?.Package is not { } package)
            return null;

        foreach (var url in package.Urls)
        {
            var bytes = await downloads.GetAsync(new Uri(url), package.Length, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
                continue;
            if (bytes.Length != package.Length || !Hashes.Matches(bytes, package.Sha256))
                throw new ToolException($"{url} does not have the hash the index records for {pluginId} {previous.Version}.");

            var path = Path.Combine(Directory.CreateDirectory(directory).FullName, BuildArtifacts.Package(pluginId, previous.Version.ToString()));
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            return path;
        }

        throw new ToolException($"The package of {pluginId} {previous.Version} could not be downloaded.");
    }
}
