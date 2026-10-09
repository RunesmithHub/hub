using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;

namespace RunesmithHub.Protocol.Packaging;

/// <summary>Checks an extracted package against its packaged manifest, and the manifest against the version record.</summary>
public static class PackageVerifier
{
    private const string LibFolder = "lib";

    /// <summary>
    /// Checks that every file the manifest lists is in <c>lib/</c> with a matching SHA-256, and that every assembly and program file in
    /// <c>lib/</c>, at any depth outside the native folder, is listed.
    /// </summary>
    /// <returns>Every problem found; empty when the files match.</returns>
    public static IReadOnlyList<string> CheckAssemblies(string stagingDirectory, PackagedManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var problems = new List<string>();
        var lib = Path.Combine(stagingDirectory, LibFolder);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, sha256) in manifest.Assemblies.All)
        {
            if (!RelativePaths.IsSafe(name) || !listed.Add(name))
            {
                problems.Add($"\"{name}\" is listed in the packaged manifest more than once or is not a safe path.");
                continue;
            }

            var file = Path.Combine(lib, name);
            if (!File.Exists(file))
            {
                problems.Add($"lib/{name} is listed in the packaged manifest but missing.");
                continue;
            }

            using var stream = File.OpenRead(file);
            if (!string.Equals(Hashes.Sha256(stream), sha256, StringComparison.Ordinal))
                problems.Add($"lib/{name} does not match its SHA-256 in the packaged manifest.");
        }

        var native = Path.Combine(lib, "native") + Path.DirectorySeparatorChar;
        var present = Directory.Exists(lib)
            ? Directory.EnumerateFiles(lib, "*", SearchOption.AllDirectories).Where(file => !file.StartsWith(native, StringComparison.Ordinal))
            : [];
        foreach (var name in present.Select(file => Path.GetRelativePath(lib, file).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal))
        {
            if (MustBeListed(name) && !listed.Contains(name))
                problems.Add($"lib/{name} is not listed in the packaged manifest.");
        }

        return problems;
    }

    private static bool MustBeListed(string name) =>
        RelativePaths.HasExtension(name, ".dll", ".exe") || PackageEntryRules.IsProcessFile($"{LibFolder}/{name}");

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
