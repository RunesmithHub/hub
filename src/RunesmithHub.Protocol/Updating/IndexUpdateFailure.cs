namespace RunesmithHub.Protocol.Updating;

/// <summary>Why an index update failed, with details for the log.</summary>
/// <param name="ExpiredAt">When the expired file expired, for the expiry kinds.</param>
public sealed record IndexUpdateFailure(IndexUpdateFailureKind Kind, string Message, DateTimeOffset? ExpiredAt = null);
