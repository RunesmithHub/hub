namespace RunesmithHub.Tool.NuGet;

/// <summary>
/// Reads packages from a local folder before asking another source: either NuGet's global packages layout
/// (<c>&lt;id&gt;/&lt;version&gt;/&lt;id&gt;.&lt;version&gt;.nupkg</c>) or a flat folder of <c>&lt;id&gt;.&lt;version&gt;.nupkg</c> files, all lowercase.
/// Every package is checked against its lock file hash afterwards, so a cache can't change the outcome.
/// </summary>
internal sealed class FolderNuGetPackageSource(string folder, INuGetPackageSource? fallback) : INuGetPackageSource
{
    public Task<byte[]> GetAsync(string id, string version, CancellationToken cancellationToken = default)
    {
        var lowerId = id.ToLowerInvariant();
        var lowerVersion = version.ToLowerInvariant();
        var fileName = $"{lowerId}.{lowerVersion}.nupkg";
        foreach (var candidate in (string[])[Path.Combine(folder, lowerId, lowerVersion, fileName), Path.Combine(folder, fileName)])
        {
            if (File.Exists(candidate))
                return File.ReadAllBytesAsync(candidate, cancellationToken);
        }

        return fallback?.GetAsync(id, version, cancellationToken)
            ?? throw new ToolException($"{id} {version} is not in {folder}.");
    }
}
