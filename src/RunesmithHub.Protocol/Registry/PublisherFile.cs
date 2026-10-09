using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Protocol.Registry;

/// <summary>A publisher's registry file, <c>publishers/&lt;name&gt;.json</c>.</summary>
public sealed record PublisherFile
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required PublisherOwner Owner { get; init; }

    public required PublisherState State { get; init; }

    /// <summary>Gets who verified the publisher and when; absent for unverified publishers.</summary>
    public PublisherVerification? Verified { get; init; }

    /// <summary>Gets raised package size limits for every plugin of the publisher.</summary>
    public SizeLimits? Limits { get; init; }
}
