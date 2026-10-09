using RunesmithHub.Protocol.Manifests;

namespace RunesmithHub.Protocol.Packaging;

/// <summary>The rules for the name and kind of each package entry.</summary>
public static class PackageEntryRules
{
    /// <summary>The prefix of declared native libraries, followed by a runtime identifier folder.</summary>
    public const string NativePrefix = "lib/native/";

    private const string LibPrefix = "lib/";

    private static readonly string[] AllowedExtensions = [".dll", ".pdb", ".xml", ".json", ".md", ".png", ".webp", ".txt"];
    private static readonly string[] NativeExtensions = [".so", ".dylib", ".dll"];
    private static readonly string[] ProcessExtensions = [".exe", ".config", ".deps.json", ".runtimeconfig.json"];
    private static readonly string[] FilesWithoutExtension = ["LICENSE"];

    /// <summary>Checks an entry name for a package whose manifest does not declare the <c>process</c> capability.</summary>
    /// <returns>The problem, or null when the name is allowed.</returns>
    public static string? Check(string name) => Check(name, declaresProcess: false);

    /// <summary>
    /// Checks an entry name. Program files under <c>lib/</c> (<c>.exe</c>, <c>.config</c>, <c>.deps.json</c>, <c>.runtimeconfig.json</c>),
    /// such as a helper process the plugin starts, are allowed only when the manifest declares the <c>process</c> capability.
    /// </summary>
    /// <returns>The problem, or null when the name is allowed.</returns>
    public static string? Check(string name, bool declaresProcess)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!RelativePaths.IsSafe(name))
            return $"\"{Printable(name)}\" is not a safe relative path of printable ASCII with forward slashes.";

        if (IsProcessFile(name))
            return declaresProcess ? null : $"\"{name}\" is a program file, which a package may only contain when it declares the process capability.";

        var isNative = name.StartsWith(NativePrefix, StringComparison.Ordinal) && name[NativePrefix.Length..].Split('/').Length == 2;
        if (isNative && RelativePaths.HasExtension(name, NativeExtensions))
            return null;
        if (FilesWithoutExtension.Contains(name, StringComparer.Ordinal))
            return null;
        if (!RelativePaths.HasExtension(name, AllowedExtensions))
            return $"\"{name}\" has a file type packages may not contain.";

        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && HostAssemblies.IsHostShared(Path.GetFileNameWithoutExtension(name)))
            return $"\"{name}\" is a copy of an assembly Runesmith shares with every plugin.";
        return null;
    }

    /// <summary>Gets whether an entry is a program file under <c>lib/</c> that needs the <c>process</c> capability.</summary>
    public static bool IsProcessFile(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(LibPrefix, StringComparison.Ordinal) && !name.StartsWith(NativePrefix, StringComparison.Ordinal)
            && RelativePaths.HasExtension(name, ProcessExtensions);
    }

    /// <summary>Gets whether a packaged manifest declares the <c>process</c> capability, which allows program files under <c>lib/</c>.</summary>
    public static bool DeclaresProcess(PackagedManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Capabilities.Any(capability => string.Equals(capability.Id, Capabilities.Process, StringComparison.Ordinal));
    }

    private static string Printable(string name) => new([.. name.Select(c => c is >= ' ' and <= '~' ? c : '?')]);
}
