using RunesmithHub.Protocol.Signing;

namespace RunesmithHub.Protocol.Index;

/// <summary>The root of trust: every role's public keys and how many signatures each role needs.</summary>
public sealed record RootPayload : SignedPayload
{
    /// <summary>Gets the public keys by key ID.</summary>
    public required IReadOnlyDictionary<string, PublicKey> Keys { get; init; }

    public required RootRoles Roles { get; init; }
}
