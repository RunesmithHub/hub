namespace RunesmithHub.Protocol.Updating;

/// <summary>Whether an index file was found.</summary>
public enum FetchStatus
{
    Found,
    NotFound,

    /// <summary>The file was larger than the limit; reading stopped at the limit.</summary>
    TooLarge,
}
