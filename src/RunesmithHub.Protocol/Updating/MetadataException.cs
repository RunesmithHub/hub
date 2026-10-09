namespace RunesmithHub.Protocol.Updating;

internal sealed class MetadataException(IndexUpdateFailureKind kind, string message, DateTimeOffset? expiredAt = null) : Exception(message)
{
    public IndexUpdateFailure ToFailure() => new(kind, Message, expiredAt);
}
