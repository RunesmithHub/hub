namespace RunesmithHub.Protocol.Updating;

/// <summary>What a trust store keeps: the newest trusted root and, after a successful update, every verified file.</summary>
/// <param name="Root">The newest trusted root envelope's bytes.</param>
/// <param name="Files">The verified files, or null before the first update or after the root replaced an online key.</param>
public sealed record TrustedIndex(ReadOnlyMemory<byte> Root, TrustedFiles? Files);
