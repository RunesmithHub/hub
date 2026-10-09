namespace RunesmithHub.Protocol.Resolution;

/// <summary>What an install plan does to one plugin.</summary>
public enum OperationKind
{
    Install,
    Update,
    Downgrade,
    Remove,

    /// <summary>Leave the installed version as it is.</summary>
    Keep,
}
