using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Build;

/// <summary>
/// Decides which files of the implementation's build output the package bundles: assemblies, native libraries, and the content files packages
/// copy to the output, which keep their folder under <c>lib/</c>. Host-shared assemblies and other plugins' contracts are left out because
/// Runesmith supplies them, and so are package assets the build left out because the shared framework supplies them.
/// Every bundled file must come from a restored package or a project in the repository.
/// </summary>
internal static class BundleCollector
{
    private const string LibFolder = "lib/";

    /// <summary>Lists the files to bundle, sorted by package path, adding a problem for each file of unknown or missing origin.</summary>
    /// <param name="declaresProcess">Whether the manifest declares the process capability, which program files need.</param>
    public static IReadOnlyList<BundleItem> Collect(
        ProjectOutput implementation, string contractsAssemblyName, SharedFrameworks frameworks, bool declaresProcess, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        ArgumentNullException.ThrowIfNull(frameworks);
        ArgumentNullException.ThrowIfNull(problems);
        var items = new Dictionary<string, BundleItem>(StringComparer.OrdinalIgnoreCase);
        var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { implementation.AssemblyFile, contractsAssemblyName + ".dll" };
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in implementation.Assets.Libraries)
        {
            var isOtherPluginsContracts = !library.IsProject && NuGetFeeds.IsContractsPackage(library.Name);
            foreach (var asset in library.RuntimeAssemblies)
            {
                var fileName = Path.GetFileName(asset);
                if (own.Contains(fileName))
                    continue;
                if (isOtherPluginsContracts || HostAssemblies.IsHostShared(Path.GetFileNameWithoutExtension(fileName)))
                {
                    excluded.Add(fileName);
                    continue;
                }

                if (!library.IsProject && !File.Exists(Path.Combine(implementation.Directory, fileName))
                    && frameworks.Supplies(library.Name, library.Version, fileName, implementation.Assets.RestoredFile(library, asset)))
                    continue;

                Add(items, implementation, library, LibFolder + fileName, fileName, asset, problems);
            }

            foreach (var native in library.NativeAssets)
            {
                var fileName = Path.GetFileName(native.Path);
                var relative = Path.Combine("runtimes", native.RuntimeIdentifier, "native", fileName);
                Add(items, implementation, library, $"{PackageEntryRules.NativePrefix}{native.RuntimeIdentifier}/{fileName}", relative, native.Path, problems);
            }

            if (!library.IsProject && !isOtherPluginsContracts)
                AddContentFiles(items, implementation, library, declaresProcess, problems);
        }

        foreach (var file in Directory.EnumerateFiles(implementation.Directory, "*.dll").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal))
        {
            if (!own.Contains(file) && !excluded.Contains(file) && !items.ContainsKey(LibFolder + file) && !HostAssemblies.IsHostShared(Path.GetFileNameWithoutExtension(file)))
                problems.Add($"{file} in the build output comes from neither a restored package nor a project; the hub only bundles assemblies it can trace.");
        }

        return [.. items.Values.OrderBy(item => item.PackagePath, StringComparer.Ordinal)];
    }

    private static void AddContentFiles(Dictionary<string, BundleItem> items, ProjectOutput implementation, AssetLibrary library, bool declaresProcess, List<string> problems)
    {
        foreach (var content in library.ContentFiles)
        {
            if (!RelativePaths.IsSafe(content.OutputPath))
            {
                problems.Add($"{library.Name} {library.Version} copies {content.Path} to \"{content.OutputPath}\", which is not a safe path in the build output.");
                continue;
            }

            if (!File.Exists(Path.Combine(implementation.Directory, content.OutputPath)))
                continue;
            var packagePath = LibFolder + content.OutputPath;
            var problem = packagePath.StartsWith(PackageEntryRules.NativePrefix, StringComparison.OrdinalIgnoreCase)
                ? $"\"{packagePath}\" is in the folder for native runtime assets."
                : PackageEntryRules.Check(packagePath, declaresProcess);
            if (problem is not null)
                problems.Add($"{content.OutputPath} from {library.Name} {library.Version} can't be bundled: {problem}");
            else
                Add(items, implementation, library, packagePath, content.OutputPath, content.Path, problems);
        }
    }

    private static void Add(
        Dictionary<string, BundleItem> items, ProjectOutput implementation, AssetLibrary library, string packagePath, string outputRelative, string asset, List<string> problems)
    {
        var source = Path.Combine(implementation.Directory, outputRelative);
        var origin = library.IsProject ? $"the project {library.Name}" : $"{library.Name} {library.Version}";
        if (!File.Exists(source))
        {
            problems.Add($"{outputRelative.Replace('\\', '/')} from {origin} is not in the build output; set EnableDynamicLoading to true in the implementation project so the build copies it.");
            return;
        }

        var item = library.IsProject
            ? new BundleItem(packagePath, source, null, null, null)
            : new BundleItem(packagePath, source, library.Name, library.Version, asset);
        if (!items.TryAdd(packagePath, item))
            problems.Add($"{packagePath} comes from more than one package.");
    }
}
