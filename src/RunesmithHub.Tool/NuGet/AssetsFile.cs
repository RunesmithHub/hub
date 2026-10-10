using System.Text.Json;

namespace RunesmithHub.Tool.NuGet;

/// <summary>A native library a package supplies for one runtime identifier.</summary>
internal sealed record NativeAsset(string Path, string RuntimeIdentifier);

/// <summary>A file a package copies to the build output, at <see cref="OutputPath"/> relative to the output folder.</summary>
internal sealed record ContentAsset(string Path, string OutputPath);

/// <summary>What restore resolved for one package or project reference, and the files it adds to the build output.</summary>
/// <param name="RuntimeAssemblies">Paths of the managed assemblies inside the package, or under the referenced project's output.</param>
internal sealed record AssetLibrary(string Name, string Version, bool IsProject, IReadOnlyList<string> RuntimeAssemblies, IReadOnlyList<NativeAsset> NativeAssets)
{
    /// <summary>Gets the package's folder under a packages folder, such as <c>markdig/0.40.0</c>, or null for projects.</summary>
    public string? PackageFolder { get; init; }

    /// <summary>Gets the content files the build copies to its output.</summary>
    public IReadOnlyList<ContentAsset> ContentFiles { get; init; } = [];
}

/// <summary>Reads the <c>obj/project.assets.json</c> restore writes, which says which package each output assembly came from.</summary>
internal sealed record AssetsFile(string TargetFramework, IReadOnlyList<AssetLibrary> Libraries)
{
    public const string FileName = "project.assets.json";

    private const string PlaceholderFile = "_._";

    /// <summary>Gets the folders restore extracted packages into, such as the global packages folder.</summary>
    public IReadOnlyList<string> PackageFolders { get; init; } = [];

    /// <summary>Gets the shared frameworks the project references, such as <c>Microsoft.NETCore.App</c>.</summary>
    public IReadOnlyList<string> FrameworkReferences { get; init; } = [];

    /// <summary>Gets the assets file restore wrote for a project, in its default intermediate folder.</summary>
    public static string PathFor(string projectFile) => Path.Combine(Path.GetDirectoryName(projectFile)!, "obj", FileName);

    /// <summary>Finds a file of a package where restore extracted it, or returns null.</summary>
    public string? RestoredFile(AssetLibrary library, string pathInPackage)
    {
        ArgumentNullException.ThrowIfNull(library);
        return library.PackageFolder is { } folder
            ? PackageFolders.Select(packages => (Root: packages, File: Path.Combine(packages, folder, pathInPackage)))
                .Where(candidate => Build.CheckoutPaths.FileExists(candidate.Root, candidate.File)).Select(candidate => candidate.File).FirstOrDefault()
            : null;
    }

    /// <exception cref="ToolException">The file is missing, not an assets file, or has more than one target framework.</exception>
    public static AssetsFile Read(string path) =>
        File.Exists(path) ? Parse(File.ReadAllBytes(path), path) : throw new ToolException($"{path} does not exist; restore the project first.");

    /// <summary>Reads an assets file that must be a regular file inside a root folder.</summary>
    /// <exception cref="ToolException">The file is missing, a symbolic link, outside the root, not an assets file, or has more than one target framework.</exception>
    public static AssetsFile Read(string root, string path) =>
        Build.CheckoutPaths.FileExists(root, path) ? Parse(Build.CheckoutPaths.ReadFile(root, path), path) : throw new ToolException($"{path} does not exist; restore the project first.");

    private static AssetsFile Parse(byte[] bytes, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var frameworks = root.GetProperty("project").GetProperty("frameworks").EnumerateObject().ToList();
            var targets = root.GetProperty("targets").EnumerateObject().Where(target => !target.Name.Contains('/', StringComparison.Ordinal)).ToList();
            if (frameworks.Count != 1 || targets.Count != 1)
                throw new ToolException($"{path} targets {frameworks.Count} frameworks; hub plugins target exactly one.");

            var folders = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("libraries", out var libraries))
            {
                foreach (var library in libraries.EnumerateObject())
                {
                    if (library.Value.TryGetProperty("path", out var folder) && folder.GetString() is { } value)
                        folders[library.Name] = value;
                }
            }

            return new AssetsFile(frameworks[0].Name, [.. targets[0].Value.EnumerateObject().Select(library => Library(library, folders))])
            {
                PackageFolders = root.TryGetProperty("packageFolders", out var packageFolders) ? [.. packageFolders.EnumerateObject().Select(folder => folder.Name)] : [],
                FrameworkReferences = frameworks[0].Value.TryGetProperty("frameworkReferences", out var references) ? [.. references.EnumerateObject().Select(reference => reference.Name)] : [],
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ToolException($"{path} is not a valid NuGet assets file: {exception.Message}", exception);
        }
    }

    private static AssetLibrary Library(JsonProperty property, Dictionary<string, string> folders)
    {
        var separator = property.Name.IndexOf('/', StringComparison.Ordinal);
        var value = property.Value;
        var runtime = value.TryGetProperty("runtime", out var assemblies)
            ? assemblies.EnumerateObject().Select(asset => asset.Name).Where(name => !name.EndsWith(PlaceholderFile, StringComparison.Ordinal)).ToList()
            : [];
        var native = value.TryGetProperty("runtimeTargets", out var targets)
            ? targets.EnumerateObject()
                .Where(asset => asset.Value.TryGetProperty("assetType", out var type) && type.GetString() == "native")
                .Select(asset => new NativeAsset(asset.Name, asset.Value.GetProperty("rid").GetString()!))
                .ToList()
            : [];
        var content = value.TryGetProperty("contentFiles", out var files)
            ? files.EnumerateObject()
                .Where(file => !file.Name.EndsWith(PlaceholderFile, StringComparison.Ordinal) && file.Value.TryGetProperty("copyToOutput", out var copy) && copy.ValueKind == JsonValueKind.True)
                .Select(file => new ContentAsset(file.Name, file.Value.GetProperty("outputPath").GetString()!))
                .ToList()
            : [];
        var isProject = value.GetProperty("type").GetString() == "project";
        return new AssetLibrary(property.Name[..separator], property.Name[(separator + 1)..], isProject, runtime, native)
        {
            PackageFolder = isProject ? null : folders.GetValueOrDefault(property.Name),
            ContentFiles = content,
        };
    }
}
