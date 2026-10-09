using System.Text.Json;

namespace RunesmithHub.Tool.NuGet;

/// <summary>A native library a package supplies for one runtime identifier.</summary>
internal sealed record NativeAsset(string Path, string RuntimeIdentifier);

/// <summary>What restore resolved for one package or project reference, and the files it adds to the build output.</summary>
/// <param name="RuntimeAssemblies">Paths of the managed assemblies inside the package, or under the referenced project's output.</param>
internal sealed record AssetLibrary(string Name, string Version, bool IsProject, IReadOnlyList<string> RuntimeAssemblies, IReadOnlyList<NativeAsset> NativeAssets);

/// <summary>Reads the <c>obj/project.assets.json</c> restore writes, which says which package each output assembly came from.</summary>
internal sealed record AssetsFile(string TargetFramework, IReadOnlyList<AssetLibrary> Libraries)
{
    public const string FileName = "project.assets.json";

    private const string PlaceholderFile = "_._";

    /// <summary>Gets the assets file restore wrote for a project, in its default intermediate folder.</summary>
    public static string PathFor(string projectFile) => Path.Combine(Path.GetDirectoryName(projectFile)!, "obj", FileName);

    /// <exception cref="ToolException">The file is missing, not an assets file, or has more than one target framework.</exception>
    public static AssetsFile Read(string path)
    {
        if (!File.Exists(path))
            throw new ToolException($"{path} does not exist; restore the project first.");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            var frameworks = root.GetProperty("project").GetProperty("frameworks").EnumerateObject().Select(framework => framework.Name).ToList();
            var targets = root.GetProperty("targets").EnumerateObject().Where(target => !target.Name.Contains('/', StringComparison.Ordinal)).ToList();
            if (frameworks.Count != 1 || targets.Count != 1)
                throw new ToolException($"{path} targets {frameworks.Count} frameworks; hub plugins target exactly one.");

            var libraries = targets[0].Value.EnumerateObject().Select(Library).ToList();
            return new AssetsFile(frameworks[0], libraries);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ToolException($"{path} is not a valid NuGet assets file: {exception.Message}", exception);
        }
    }

    private static AssetLibrary Library(JsonProperty property)
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
        return new AssetLibrary(property.Name[..separator], property.Name[(separator + 1)..], value.GetProperty("type").GetString() == "project", runtime, native);
    }
}
