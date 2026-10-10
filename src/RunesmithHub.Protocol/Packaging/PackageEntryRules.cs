using RunesmithHub.Protocol.Manifests;

namespace RunesmithHub.Protocol.Packaging;

/// <summary>The rules for the name and kind of each package entry.</summary>
public static class PackageEntryRules
{
    /// <summary>The prefix of declared native libraries, followed by a runtime identifier folder.</summary>
    public const string NativePrefix = "lib/native/";

    /// <summary>The prefix of the files web views show, from the plugin's <c>web</c> folder.</summary>
    public const string WebPrefix = "web/";

    private const string LibPrefix = "lib/";

    private static readonly string[] AllowedExtensions = [".dll", ".pdb", ".xml", ".json", ".md", ".png", ".webp", ".txt"];
    private static readonly string[] NativeExtensions = [".so", ".dylib", ".dll"];
    private static readonly string[] ProcessExtensions = [".exe", ".config", ".deps.json", ".runtimeconfig.json"];
    private static readonly string[] FilesWithoutExtension = ["LICENSE"];

    private static readonly string[] WebExtensions =
    [
        ".html", ".htm", ".css", ".js", ".mjs", ".map", ".svg", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".woff", ".woff2", ".ttf",
        ".otf", ".wasm", ".json", ".txt", ".mp3", ".mp4", ".webm",
    ];

    private static readonly string[] WebCodeExtensions = [".js", ".mjs", ".wasm"];

    /// <summary>Checks an entry name for a package whose manifest does not declare the <c>process</c> capability.</summary>
    /// <returns>The problem, or null when the name is allowed.</returns>
    public static string? Check(string name) => Check(name, declaresProcess: false);

    /// <summary>
    /// Checks an entry name. Files under <c>web/</c> may only be of the types web pages use. Program files under <c>lib/</c> (<c>.exe</c>, <c>.config</c>, <c>.deps.json</c>, <c>.runtimeconfig.json</c>),
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

        if (IsWebFile(name))
            return RelativePaths.HasExtension(name, WebExtensions) ? null : $"\"{name}\" has a file type web pages may not contain.";
        if (name.StartsWith(WebPrefix, StringComparison.OrdinalIgnoreCase))
            return $"\"{name}\" must be in the web folder, spelled in lowercase.";

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

    /// <summary>Gets whether an entry is under <c>web/</c>, the folder web views show.</summary>
    public static bool IsWebFile(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(WebPrefix, StringComparison.Ordinal);
    }

    /// <summary>Gets whether an entry is code web views run: JavaScript or WebAssembly under <c>web/</c>.</summary>
    public static bool IsWebCode(string name) => IsWebFile(name) && RelativePaths.HasExtension(name, WebCodeExtensions);

    /// <summary>Gets whether a packaged manifest declares the <c>process</c> capability, which allows program files under <c>lib/</c>.</summary>
    public static bool DeclaresProcess(PackagedManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Capabilities.Any(capability => string.Equals(capability.Id, Capabilities.Process, StringComparison.Ordinal));
    }

    private static string Printable(string name) => new([.. name.Select(c => c is >= ' ' and <= '~' ? c : '?')]);
}
