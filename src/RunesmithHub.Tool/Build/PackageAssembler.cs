using System.Text;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Tool.Images;
using RunesmithHub.Tool.Json;
using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Build;

/// <summary>
/// Turns a restored and built plugin into everything the hub publishes for a version: the package, the re-encoded images, the SBOM, the
/// source archive and the list of every file's hash.
/// </summary>
/// <param name="dotnetRoots">The .NET installations whose targeting packs say which assemblies the shared frameworks supply.</param>
internal sealed class PackageAssembler(BundleVerifier verifier, IReadOnlyList<string> dotnetRoots)
{
    private const string LibFolder = "lib/";
    private const string WebFolder = "web";
    private static readonly int[] PackagedIconSizes = [64, 128];

    /// <exception cref="ToolException">The build output can't be packaged; the message lists every problem.</exception>
    /// <exception cref="PackageException">The package would break a packaging rule.</exception>
    public async Task<PackageResult> AssembleAsync(PackageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var output = PrepareOutput(request.Output);
        var source = PluginSource.Load(request.PluginRoot, new PluginExpectations(null, null, null));
        var root = source.Root;
        var contracts = ProjectOutput.Locate(root, source.ContractsProject);
        var implementation = ProjectOutput.Locate(root, source.ImplementationProject);

        var problems = new List<string>();
        var declaresProcess = source.Manifest.Capabilities?.Any(capability => capability.Id == Capabilities.Process) == true;
        var items = BundleCollector.Collect(implementation, contracts.AssemblyName, SharedFrameworks.Locate(implementation.Assets, dotnetRoots), declaresProcess, problems);
        var locked = source.LockedPackages();
        var bundled = await verifier.VerifyAsync(items, locked, problems, cancellationToken).ConfigureAwait(false);
        if (problems.Count > 0)
            throw new ToolException("The build output can't be packaged:\n" + string.Join("\n", problems.Select(problem => $"  {problem}")));

        var images = ImageEncoder.Encode(source.Root, source.Manifest);
        var id = source.Id;
        var version = source.Version.ToString();
        var sbom = CycloneDxSbom.Create(id, version, request.Commit, source.Manifest.Repository!, locked, bundled);
        var files = new List<PackageFile>();
        var assemblies = new PackagedAssemblies
        {
            Contracts = AddAssembly(files, root, contracts, withDocumentation: true),
            Implementation = AddAssembly(files, root, implementation, withDocumentation: false),
            Bundled = [.. items.Where(item => !item.IsNative).Select(item => Bundled(item, AddFile(files, root, item.PackagePath, item.SourceFile)))],
        };
        foreach (var item in items.Where(item => item.IsNative))
            AddFile(files, root, item.PackagePath, item.SourceFile);

        var manifest = PackagedManifest.Create(source.Manifest, assemblies, new PackageBuild { Commit = request.Commit, Run = request.Run, Sdk = request.DotnetSdk });
        files.Add(new PackageFile(DeveloperManifest.FileName, IndexJson.Serialize(manifest)));
        files.AddRange(images.Where(image => image.Kind == "icon" && PackagedIconSizes.Contains(image.Width)).Select(image => new PackageFile($"icon/{image.Width}.png", image.Content)));
        AddFile(files, root, "LICENSE", source.Resolve("LICENSE"));
        AddFile(files, root, "README.md", source.Resolve("README.md"));
        AddWebFiles(files, source);
        files.Add(new PackageFile(ThirdPartyNotices.FileName, Encoding.UTF8.GetBytes(ThirdPartyNotices.Create(source.Manifest.Name!, bundled))));
        files.Add(new PackageFile(CycloneDxSbom.FileName, sbom));

        var package = PackageWriter.Write(files, request.Limits);
        var packageFile = Path.Combine(output, BuildArtifacts.Package(id, version));
        await File.WriteAllBytesAsync(packageFile, package, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(output, BuildArtifacts.Sbom(id, version)), sbom, cancellationToken).ConfigureAwait(false);
        var hubPackages = LockFile.Read(root, LockFile.PathFor(source.ContractsProject)).Where(package => NuGetFeeds.IsHubPackage(package.Id));
        var contractsPackage = ContractsNuGetPackage.Create(manifest, contracts, Content(files, LibFolder + contracts.AssemblyFile)!, Content(files, LibFolder + contracts.DocumentationFile), hubPackages);
        await File.WriteAllBytesAsync(Path.Combine(output, BuildArtifacts.ContractsPackage(contracts.AssemblyFile, version)), contractsPackage, cancellationToken).ConfigureAwait(false);
        await WriteImagesAsync(output, images, cancellationToken).ConfigureAwait(false);
        if (request.SourceArchive is { } archive)
            File.Copy(archive, Path.Combine(output, BuildArtifacts.Source(id, version)));

        var hashes = HashList.Create(output);
        hashes.Write(output);
        return new PackageResult(packageFile, manifest, hashes);
    }

    private static string PrepareOutput(string directory)
    {
        var output = Directory.CreateDirectory(directory).FullName;
        return Directory.EnumerateFileSystemEntries(output).Any() ? throw new ToolException($"{output} is not empty.") : output;
    }

    private static AssemblyFile AddAssembly(List<PackageFile> files, string root, ProjectOutput project, bool withDocumentation)
    {
        var sha256 = AddFile(files, root, LibFolder + project.AssemblyFile, project.AssemblyPath);
        var symbols = Path.ChangeExtension(project.AssemblyPath, ".pdb");
        if (CheckoutPaths.FileExists(root, symbols))
            AddFile(files, root, LibFolder + Path.GetFileName(symbols), symbols);
        var documentation = Path.Combine(project.Directory, project.DocumentationFile);
        if (withDocumentation && CheckoutPaths.FileExists(root, documentation))
            AddFile(files, root, LibFolder + project.DocumentationFile, documentation);
        return new AssemblyFile { File = project.AssemblyFile, Sha256 = sha256 };
    }

    private static string AddFile(List<PackageFile> files, string root, string packagePath, string sourceFile)
    {
        var content = CheckoutPaths.ReadFile(root, sourceFile);
        files.Add(new PackageFile(packagePath, content));
        return Hashes.Sha256(content);
    }

    /// <summary>Adds every file of the <c>web</c> folder next to the manifest, which web views show, at its path under <c>web/</c>.</summary>
    /// <exception cref="ToolException">The folder or something in it is a symbolic link.</exception>
    private static void AddWebFiles(List<PackageFile> files, PluginSource source)
    {
        var web = source.Resolve(WebFolder);
        if (!Directory.Exists(web))
            return;
        CheckoutPaths.EnsureNoLinks(source.Root, web);
        foreach (var file in RegularFiles(source.Root, web))
            AddFile(files, source.Root, Path.GetRelativePath(source.Root, file).Replace(Path.DirectorySeparatorChar, '/'), file);
    }

    private static IEnumerable<string> RegularFiles(string root, string directory)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false };
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", options).Order(StringComparer.Ordinal))
        {
            if (new FileInfo(entry).LinkTarget is not null)
                throw new ToolException($"{Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/')} is a symbolic link; the hub reads only regular files and folders.");
            if (Directory.Exists(entry))
            {
                foreach (var file in RegularFiles(root, entry))
                    yield return file;
            }
            else
                yield return entry;
        }
    }

    private static byte[]? Content(List<PackageFile> files, string packagePath) =>
        files.FirstOrDefault(file => string.Equals(file.Path, packagePath, StringComparison.Ordinal))?.Content.ToArray();

    private static BundledAssembly Bundled(BundleItem item, string sha256) =>
        new() { File = item.LibPath, Sha256 = sha256, Package = item.PackageId, PackageVersion = item.PackageVersion };

    private static async Task WriteImagesAsync(string output, IReadOnlyList<EncodedImage> images, CancellationToken cancellationToken)
    {
        var folder = Directory.CreateDirectory(Path.Combine(output, ImageEncoder.Folder)).FullName;
        foreach (var image in images)
            await File.WriteAllBytesAsync(Path.Combine(folder, image.File), image.Content.ToArray(), cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(folder, ImageList.FileName), ArtifactJson.Serialize(new ImageList(images)), cancellationToken).ConfigureAwait(false);
    }
}
