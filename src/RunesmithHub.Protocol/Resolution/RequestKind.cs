namespace RunesmithHub.Protocol.Resolution;

/// <summary>What the user asked for.</summary>
public enum RequestKind
{
    /// <summary>Install a plugin, at any version or a specific one.</summary>
    Install,

    /// <summary>Update one plugin to the highest version that fits.</summary>
    Update,

    /// <summary>Update every installed plugin.</summary>
    UpdateAll,

    /// <summary>Remove plugins.</summary>
    Remove,

    /// <summary>Change nothing on purpose; repair what the index's states require, such as moving off a blocked version.</summary>
    Sync,
}
