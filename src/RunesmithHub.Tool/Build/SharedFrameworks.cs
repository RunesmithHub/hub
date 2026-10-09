using System.Runtime.InteropServices;
using Mono.Cecil;
using NuGet.Frameworks;
using NuGet.Versioning;
using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Build;

/// <summary>
/// The assemblies the shared frameworks a project references supply, read from their targeting packs' platform manifests and package
/// overrides. The SDK leaves a package's runtime asset out of the build output when the framework's copy wins conflict resolution.
/// </summary>
internal sealed class SharedFrameworks
{
    private const string FileVersionAttribute = "System.Reflection.AssemblyFileVersionAttribute";

    private readonly Dictionary<string, (Version Assembly, Version File)> _files;
    private readonly Dictionary<string, NuGetVersion> _overrides;

    private SharedFrameworks(Dictionary<string, (Version Assembly, Version File)> files, Dictionary<string, NuGetVersion> overrides)
    {
        _files = files;
        _overrides = overrides;
    }

    /// <summary>Gets frameworks that supply nothing, for a project whose targeting packs could not be found.</summary>
    public static SharedFrameworks None { get; } = new(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase));

    /// <summary>Gets the .NET installations to look for targeting packs in: <c>DOTNET_ROOT</c>, then the one running the tool.</summary>
    public static IReadOnlyList<string> DefaultRoots(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        var running = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        return getEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root ? [root, running] : [running];
    }

    /// <summary>
    /// Reads the targeting pack of each framework the project references from the first installation that has one for the project's .NET
    /// version; a framework without a pack supplies nothing.
    /// </summary>
    public static SharedFrameworks Locate(AssetsFile assets, IEnumerable<string> dotnetRoots)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(dotnetRoots);
        var roots = dotnetRoots.ToList();
        var target = NuGetFramework.Parse(assets.TargetFramework).Version;
        var files = new Dictionary<string, (Version Assembly, Version File)>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, NuGetVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var framework in assets.FrameworkReferences)
        {
            if (roots.Select(root => PackData(root, framework, target)).FirstOrDefault(data => data is not null) is not { } data)
                continue;
            ReadPlatformManifest(Path.Combine(data, "PlatformManifest.txt"), files);
            ReadPackageOverrides(Path.Combine(data, "PackageOverrides.txt"), overrides);
        }

        return new SharedFrameworks(files, overrides);
    }

    /// <summary>
    /// Gets whether a framework's copy of a package's runtime asset wins conflict resolution: the framework overrides that package version,
    /// or its assembly has a higher assembly version, or the same one and at least the same file version.
    /// </summary>
    /// <param name="restoredAsset">The asset where restore extracted it, to read its versions from; null when it can't be found.</param>
    public bool Supplies(string packageId, string packageVersion, string fileName, string? restoredAsset)
    {
        if (!_files.TryGetValue(fileName, out var framework))
            return false;
        if (_overrides.TryGetValue(packageId, out var overridden) && NuGetVersion.TryParse(packageVersion, out var version) && version <= overridden)
            return true;
        if (restoredAsset is null || ReadVersions(restoredAsset) is not { } package)
            return false;
        var byAssembly = framework.Assembly.CompareTo(package.Assembly);
        return byAssembly > 0 || (byAssembly == 0 && framework.File >= package.File);
    }

    private static string? PackData(string root, string framework, Version target)
    {
        var pack = Path.Combine(root, "packs", framework + ".Ref");
        if (!Directory.Exists(pack))
            return null;
        var version = Directory.EnumerateDirectories(pack)
            .Select(Path.GetFileName)
            .Select(name => NuGetVersion.TryParse(name, out var parsed) ? parsed : null)
            .OfType<NuGetVersion>()
            .Where(candidate => candidate.Major == target.Major && candidate.Minor == target.Minor)
            .Max();
        return version is null ? null : Path.Combine(pack, version.OriginalVersion!, "data");
    }

    // Lines are "file|pack|assembly version|file version"; native files have no assembly version.
    private static void ReadPlatformManifest(string path, Dictionary<string, (Version Assembly, Version File)> files)
    {
        if (!File.Exists(path))
            return;
        foreach (var fields in File.ReadLines(path).Select(line => line.Split('|')).Where(fields => fields.Length == 4))
        {
            if (Version.TryParse(fields[2], out var assembly) && Version.TryParse(fields[3], out var file))
                files[fields[0]] = files.TryGetValue(fields[0], out var known) && known.Assembly > assembly ? known : (assembly, file);
        }
    }

    // Lines are "package id|highest version the framework replaces".
    private static void ReadPackageOverrides(string path, Dictionary<string, NuGetVersion> overrides)
    {
        if (!File.Exists(path))
            return;
        foreach (var fields in File.ReadLines(path).Select(line => line.Split('|')).Where(fields => fields.Length == 2))
        {
            if (NuGetVersion.TryParse(fields[1], out var version) && (!overrides.TryGetValue(fields[0], out var known) || version > known))
                overrides[fields[0]] = version;
        }
    }

    private static (Version Assembly, Version File)? ReadVersions(string path)
    {
        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            var fileVersion = assembly.CustomAttributes
                .Where(attribute => attribute.AttributeType.FullName == FileVersionAttribute && attribute.ConstructorArguments.Count == 1)
                .Select(attribute => attribute.ConstructorArguments[0].Value as string)
                .FirstOrDefault();
            return (assembly.Name.Version, Version.TryParse(fileVersion, out var file) ? file : new Version(0, 0, 0, 0));
        }
        catch (Exception exception) when (exception is BadImageFormatException or InvalidOperationException or ArgumentException or IOException)
        {
            return null;
        }
    }
}
