namespace RunesmithHub.Protocol.Resolution;

/// <summary>Why resolution produced no plan.</summary>
public enum ResolutionFailureKind
{
    /// <summary>No combination of versions satisfies every rule.</summary>
    NoSolution,

    /// <summary>The plugin to remove is needed by other installed plugins, listed in <see cref="ResolutionFailure.Dependents"/>.</summary>
    NeededByOthers,

    /// <summary>The requested plugin or version is not in the index.</summary>
    NotFound,
}
