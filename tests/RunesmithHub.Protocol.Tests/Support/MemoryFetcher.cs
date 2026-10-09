using RunesmithHub.Protocol.Updating;

namespace RunesmithHub.Protocol.Tests.Support;

/// <summary>Serves files from a dictionary, honouring size limits, and records what was asked for.</summary>
internal sealed class MemoryFetcher(Dictionary<string, byte[]> files) : IIndexFetcher
{
    public bool Offline { get; set; }

    public List<string> Requests { get; } = [];

    public Task<FetchResult> FetchAsync(string path, long maxBytes, CancellationToken cancellationToken)
    {
        Requests.Add(path);
        if (Offline)
            throw new IndexUnavailableException("No network.");
        if (!files.TryGetValue(path, out var bytes))
            return Task.FromResult(FetchResult.NotFound);
        return Task.FromResult(bytes.Length > maxBytes ? FetchResult.TooLarge : FetchResult.Found(bytes));
    }
}
