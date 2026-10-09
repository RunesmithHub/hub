namespace RunesmithHub.Tool.Publishing;

/// <summary>What pushing a package to the hub feed did.</summary>
internal enum FeedPushResult
{
    Pushed,
    AlreadyThere,
}

/// <summary>The hub's NuGet feed.</summary>
internal interface IFeedPublisher
{
    /// <summary>Pushes a package unless the feed already has it with the same hash.</summary>
    /// <exception cref="ToolException">The feed has the package with different bytes, or the push failed.</exception>
    Task<FeedPushResult> PushAsync(string packageFile, string packageId, string version, string sha256, CancellationToken cancellationToken);

    /// <summary>Removes a package from the feed; returns false when the feed does not have it.</summary>
    /// <exception cref="ToolException">The removal failed.</exception>
    Task<bool> DeleteAsync(string packageId, string version, CancellationToken cancellationToken);
}
