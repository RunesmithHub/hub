namespace RunesmithHub.Protocol.Registry;

/// <summary>The repository a registration binds a plugin to.</summary>
public sealed record RegisteredRepository
{
    /// <summary>Gets the repository's permanent numeric ID.</summary>
    public required long Id { get; init; }

    /// <summary>Gets the URL, <c>https://github.com/&lt;owner&gt;/&lt;repo&gt;</c>.</summary>
    public required string Url { get; init; }

    // Source-generated JSON passes null for omitted init-only properties, so the setters restore the defaults.
    /// <summary>Gets the plugin's folder in the repository; <c>.</c> for the root.</summary>
    public string Path { get; init => field = value ?? "."; } = ".";

    /// <summary>Gets what release tags start with before the version.</summary>
    public string TagPrefix { get; init => field = value ?? "v"; } = "v";
}
