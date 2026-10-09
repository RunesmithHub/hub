using System.Security.Cryptography;

namespace RunesmithHub.Protocol;

/// <summary>SHA-256 in the hub's text form: lowercase hex.</summary>
public static class Hashes
{
    public static string Sha256(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static string Sha256(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));

    /// <summary>Gets whether text is a lowercase hex SHA-256.</summary>
    public static bool IsSha256(string? text) => text is { Length: 64 } && text.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>Compares a computed hash with an expected one in constant time.</summary>
    public static bool Matches(ReadOnlySpan<byte> data, string expected) =>
        IsSha256(expected) && CryptographicOperations.FixedTimeEquals(SHA256.HashData(data), Convert.FromHexString(expected));
}
