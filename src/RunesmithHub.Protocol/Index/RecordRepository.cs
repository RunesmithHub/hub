namespace RunesmithHub.Protocol.Index;

/// <summary>The GitHub repository a plugin is built from.</summary>
public sealed record RecordRepository
{
    /// <summary>Gets the repository's permanent numeric ID.</summary>
    public required long Id { get; init; }

    public required string Url { get; init; }

    /// <summary>Gets the plugin's folder in the repository; <c>.</c> for the root.</summary>
    public required string Path { get; init; }
}
