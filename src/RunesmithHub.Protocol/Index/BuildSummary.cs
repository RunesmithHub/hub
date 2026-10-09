namespace RunesmithHub.Protocol.Index;

/// <summary>How a version was built.</summary>
public sealed record BuildSummary
{
    /// <summary>Gets the URL of the pipeline run that built it.</summary>
    public required string Run { get; init; }

    /// <summary>Gets the .NET SDK version used.</summary>
    public required string Sdk { get; init; }

    /// <summary>Gets whether two independent builds matched byte for byte.</summary>
    public required bool Reproducible { get; init; }
}
