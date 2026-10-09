namespace RunesmithHub.Protocol.Updating;

/// <summary>Gets index files from a local folder, such as a development fixture.</summary>
public sealed class DirectoryIndexFetcher(string directory) : IIndexFetcher
{
    public async Task<FetchResult> FetchAsync(string path, long maxBytes, CancellationToken cancellationToken)
    {
        if (!RelativePaths.IsSafe(path))
            return FetchResult.NotFound;

        var file = Path.Combine(directory, path);
        if (!File.Exists(file))
            return FetchResult.NotFound;

        await using var stream = File.OpenRead(file);
        return await LimitedReader.ReadAsync(stream, maxBytes, cancellationToken).ConfigureAwait(false);
    }
}
