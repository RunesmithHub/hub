using RunesmithHub.Protocol.Manifests;

namespace RunesmithHub.Tool.Build;

/// <summary>What a packaging run wrote: the package file, its manifest, and the hash list of the whole output folder.</summary>
internal sealed record PackageResult(string PackageFile, PackagedManifest Manifest, HashList Hashes);
