namespace RunesmithHub.Protocol.Updating;

/// <summary>Gets index files from wherever the index is hosted.</summary>
public interface IIndexFetcher
{
    /// <summary>Gets a file by its path relative to the index root, reading no more than <paramref name="maxBytes"/>.</summary>
    /// <exception cref="IndexUnavailableException">The host could not be reached or did not answer properly.</exception>
    Task<FetchResult> FetchAsync(string path, long maxBytes, CancellationToken cancellationToken);
}
