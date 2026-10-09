using RunesmithHub.Protocol.Signing;

namespace RunesmithHub.Tool.Keys;

/// <summary>What a new root holds: its version, expiry, and each role's public keys and threshold.</summary>
internal sealed record RootTemplate(int Version, DateTimeOffset Expires, RoleTemplate Root, RoleTemplate Publish, RoleTemplate Moderation, RoleTemplate Timestamp);

/// <summary>A role's public keys and how many of them must sign.</summary>
internal sealed record RoleTemplate(IReadOnlyList<PublicKey> Keys, int Threshold);
