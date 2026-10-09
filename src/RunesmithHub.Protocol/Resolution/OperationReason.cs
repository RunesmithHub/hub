namespace RunesmithHub.Protocol.Resolution;

/// <summary>Why a plugin is in an install plan.</summary>
public enum OperationReason
{
    /// <summary>The user asked for it, now or when installing it.</summary>
    Requested,

    /// <summary>Another plugin in the plan needs it.</summary>
    Dependency,

    /// <summary>It came in as a dependency and nothing needs it anymore.</summary>
    NoLongerNeeded,

    /// <summary>No version of it can be used anymore, for example because it is blocked.</summary>
    Unavailable,
}
