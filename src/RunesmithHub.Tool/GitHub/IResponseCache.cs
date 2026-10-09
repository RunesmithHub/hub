namespace RunesmithHub.Tool.GitHub;

/// <summary>A response kept for conditional requests: its entity tag, body and the link to the next page.</summary>
internal sealed record CachedResponse(string ETag, string Body, string? Next);

/// <summary>Keeps GitHub responses by URL so unchanged resources are asked for with <c>If-None-Match</c> and cost no rate limit.</summary>
internal interface IResponseCache
{
    CachedResponse? Get(string url);

    void Set(string url, CachedResponse response);
}

/// <summary>A cache that keeps nothing.</summary>
internal sealed class NoResponseCache : IResponseCache
{
    public static NoResponseCache Instance { get; } = new();

    public CachedResponse? Get(string url) => null;

    public void Set(string url, CachedResponse response)
    {
    }
}
