namespace RunesmithHub.Protocol.Manifests;

/// <summary>Where a package was built from.</summary>
public sealed record PackageBuild
{
    /// <summary>Gets the full SHA of the commit.</summary>
    public required string Commit { get; init; }

    /// <summary>Gets the URL of the pipeline run, or <c>local</c>.</summary>
    public required string Run { get; init; }

    /// <summary>Gets the .NET SDK version.</summary>
    public required string Sdk { get; init; }
}
