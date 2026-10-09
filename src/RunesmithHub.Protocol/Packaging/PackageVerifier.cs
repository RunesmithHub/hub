using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;

namespace RunesmithHub.Protocol.Packaging;

/// <summary>Checks an extracted package against its packaged manifest, and the manifest against the version record.</summary>
public static class PackageVerifier
{
    private const string LibFolder = "lib";

    /// <summary>
    /// Checks that every assembly in <c>lib/</c>, outside the native folder, is listed in the manifest with a matching SHA-256, and that every
    /// listed assembly is there.
    /// </summary>
    /// <returns>Every problem found; empty when the assemblies match.</returns>
    public static IReadOnlyList<string> CheckAssemblies(string stagingDirectory, PackagedManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var problems = new List<string>();
        var lib = Path.Combine(stagingDirectory, LibFolder);
        var listed = manifest.Assemblies.All.ToDictionary(assembly => assembly.File, assembly => assembly.Sha256, StringComparer.Ordinal);
        var native = Path.Combine(lib, "native") + Path.DirectorySeparatorChar;
        var present = Directory.Exists(lib)
            ? Directory.EnumerateFiles(lib, "*.dll", SearchOption.AllDirectories).Where(file => !file.StartsWith(native, StringComparison.Ordinal)).ToList()
            : [];

        foreach (var file in present)
        {
            var name = Path.GetRelativePath(lib, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!listed.TryGetValue(name, out var sha256))
            {
                problems.Add($"lib/{name} is not listed in the packaged manifest.");
                continue;
            }

            using var stream = File.OpenRead(file);
            if (!string.Equals(Hashes.Sha256(stream), sha256, StringComparison.Ordinal))
                problems.Add($"lib/{name} does not match its SHA-256 in the packaged manifest.");
        }

        var presentNames = present.Select(file => Path.GetRelativePath(lib, file).Replace(Path.DirectorySeparatorChar, '/')).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(listed.Keys.Where(name => !presentNames.Contains(name)).Select(name => $"lib/{name} is listed in the packaged manifest but missing."));
        return problems;
    }

    /// <summary>Checks that the packaged manifest's ID, version, API range, dependencies and capabilities match the version record.</summary>
    /// <returns>Every difference found; empty when they match.</returns>
    public static IReadOnlyList<string> CheckAgainstRecord(PackagedManifest manifest, string pluginId, VersionRecord record)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(record);
        var problems = new List<string>();
        if (!string.Equals(manifest.Id, pluginId, StringComparison.Ordinal))
            problems.Add($"The package is {manifest.Id}, not {pluginId}.");
        if (manifest.Version != record.Version)
            problems.Add($"The package is version {manifest.Version}, not {record.Version}.");
        if (!manifest.RunesmithApi.Equals(record.RunesmithApi))
            problems.Add($"The package's Runesmith API range {manifest.RunesmithApi} differs from the record's {record.RunesmithApi}.");

        static string Dependency(PluginDependency dependency) => $"{dependency.Id} {dependency.Range}";
        if (!manifest.Dependencies.Select(Dependency).ToHashSet(StringComparer.Ordinal).SetEquals(record.Dependencies.Select(Dependency)))
            problems.Add("The package's dependencies differ from the record's.");

        static string Capability(DeclaredCapability capability) => $"{capability.Id}\n{capability.Reason}";
        if (!manifest.Capabilities.Select(Capability).ToHashSet(StringComparer.Ordinal).SetEquals(record.Capabilities.Declared.Select(Capability)))
            problems.Add("The package's capabilities differ from the record's.");
        return problems;
    }
}
