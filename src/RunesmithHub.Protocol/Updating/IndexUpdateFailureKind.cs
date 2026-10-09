namespace RunesmithHub.Protocol.Updating;

/// <summary>Why an index update did not produce a new view.</summary>
public enum IndexUpdateFailureKind
{
    /// <summary>The index host could not be reached; the saved view still applies.</summary>
    Offline,

    /// <summary>The newest trusted root has expired; Runesmith needs an update.</summary>
    RootExpired,

    /// <summary>The timestamp, snapshot or moderation list has expired, so the hub's safety status can't be confirmed.</summary>
    MetadataExpired,

    /// <summary>A signature, hash, length, size or version check failed; nothing was saved.</summary>
    VerificationFailed,
}
