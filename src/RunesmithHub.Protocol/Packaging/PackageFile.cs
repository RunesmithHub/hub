namespace RunesmithHub.Protocol.Packaging;

/// <summary>A file to put in a package, at a relative path with forward slashes.</summary>
public sealed record PackageFile(string Path, ReadOnlyMemory<byte> Content);
