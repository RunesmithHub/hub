using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Protocol.Registry;

/// <summary>A plugin's registration, <c>plugins/&lt;id&gt;/registration.json</c>: the binding of its ID to a repository and maintainers.</summary>
public sealed record Registration
{
    public required string Id { get; init; }

    public required RegisteredRepository Repository { get; init; }

    public required IReadOnlyList<GitHubAccount> Maintainers { get; init; }

    public required PluginTier Tier { get; init; }

    public required RegistrationState State { get; init; }

    public required DateTimeOffset RegisteredAt { get; init; }

    /// <summary>Gets the deprecation a maintainer set through the registry; it overrides the newest manifest's.</summary>
    public Deprecation? Deprecated { get; init; }

    public SizeLimits? Limits { get; init; }
}
