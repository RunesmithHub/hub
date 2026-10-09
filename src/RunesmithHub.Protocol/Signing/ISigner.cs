namespace RunesmithHub.Protocol.Signing;

/// <summary>Something that signs with one key, such as a key in memory or a hardware token.</summary>
public interface ISigner
{
    /// <summary>Gets the ID of the key that signs.</summary>
    string KeyId { get; }

    /// <summary>Signs data with ECDSA P-256 and SHA-256, returning the 64-byte IEEE P1363 signature.</summary>
    byte[] Sign(ReadOnlySpan<byte> data);
}
