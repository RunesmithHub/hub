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
internal sealed class PackageAssembler(BundleVerifier verifier, SourceArchiver archiver, IReadOnlyList<string> dotnetRoots)
{
    private const string LibFolder = "lib/";
    private static readonly int[] PackagedIconSizes = [64, 128];

    /// <exception cref="ToolException">The build output can't be packaged; the message lists every problem.</exception>
    /// <exception cref="PackageException">The package would break a packaging rule.</exception>
    public async Task<PackageResult> AssembleAsync(PackageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var output = PrepareOutput(request.Output);
        var source = PluginSource.Load(request.PluginRoot, new PluginExpectations(null, null, null));
        var contracts = ProjectOutput.Locate(source.ContractsProject);
        var implementation = ProjectOutput.Locate(source.ImplementationProject);

        var problems = new List<string>();
        var items = BundleCollector.Collect(implementation, contracts.AssemblyName, SharedFrameworks.Locate(implementation.Assets, dotnetRoots), problems);
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
            Contracts = AddAssembly(files, contracts, withDocumentation: true),
            Implementation = AddAssembly(files, implementation, withDocumentation: false),
            Bundled = [.. items.Where(item => !item.IsNative).Select(item => Bundled(item, AddFile(files, item.PackagePath, item.SourceFile)))],
        };
        foreach (var item in items.Where(item => item.IsNative))
            AddFile(files, item.PackagePath, item.SourceFile);

        var manifest = PackagedManifest.Create(source.Manifest, assemblies, new PackageBuild { Commit = request.Commit, Run = request.Run, Sdk = request.DotnetSdk });
        files.Add(new PackageFile(DeveloperManifest.FileName, IndexJson.Serialize(manifest)));
        files.AddRange(images.Where(image => image.Kind == "icon" && PackagedIconSizes.Contains(image.Width)).Select(image => new PackageFile($"icon/{image.Width}.png", image.Content)));
        AddFile(files, "LICENSE", source.Resolve("LICENSE"));
        AddFile(files, "README.md", source.Resolve("README.md"));
        files.Add(new PackageFile(ThirdPartyNotices.FileName, Encoding.UTF8.GetBytes(ThirdPartyNotices.Create(source.Manifest.Name!, bundled))));
        files.Add(new PackageFile(CycloneDxSbom.FileName, sbom));

        var package = PackageWriter.Write(files, request.Limits);
        var packageFile = Path.Combine(output, BuildArtifacts.Package(id, version));
        await File.WriteAllBytesAsync(packageFile, package, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(output, BuildArtifacts.Sbom(id, version)), sbom, cancellationToken).ConfigureAwait(false);
        var hubPackages = LockFile.Read(LockFile.PathFor(source.ContractsProject)).Where(package => NuGetFeeds.IsHubPackage(package.Id));
        await File.WriteAllBytesAsync(Path.Combine(output, BuildArtifacts.ContractsPackage(contracts.AssemblyFile, version)), ContractsNuGetPackage.Create(manifest, contracts, hubPackages), cancellationToken).ConfigureAwait(false);
        await WriteImagesAsync(output, images, cancellationToken).ConfigureAwait(false);
        if (request.ArchiveSource)
        {
            var archive = await archiver.CreateAsync(source.Root, request.Commit, $"{id}-{version}", cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(output, BuildArtifacts.Source(id, version)), archive, cancellationToken).ConfigureAwait(false);
        }

        var hashes = HashList.Create(output);
        hashes.Write(output);
        return new PackageResult(packageFile, manifest, hashes);
    }

    private static string PrepareOutput(string directory)
    {
        var output = Directory.CreateDirectory(directory).FullName;
        return Directory.EnumerateFileSystemEntries(output).Any() ? throw new ToolException($"{output} is not empty.") : output;
    }

    private static AssemblyFile AddAssembly(List<PackageFile> files, ProjectOutput project, bool withDocumentation)
    {
        var sha256 = AddFile(files, LibFolder + project.AssemblyFile, project.AssemblyPath);
        var symbols = Path.ChangeExtension(project.AssemblyPath, ".pdb");
        if (File.Exists(symbols))
            AddFile(files, LibFolder + Path.GetFileName(symbols), symbols);
        var documentation = Path.ChangeExtension(project.AssemblyPath, ".xml");
        if (withDocumentation && File.Exists(documentation))
            AddFile(files, LibFolder + Path.GetFileName(documentation), documentation);
        return new AssemblyFile { File = project.AssemblyFile, Sha256 = sha256 };
    }

    private static string AddFile(List<PackageFile> files, string packagePath, string sourceFile)
    {
        var content = File.ReadAllBytes(sourceFile);
        files.Add(new PackageFile(packagePath, content));
        return Hashes.Sha256(content);
    }

    private static BundledAssembly Bundled(BundleItem item, string sha256) =>
        new() { File = item.FileName, Sha256 = sha256, Package = item.PackageId, PackageVersion = item.PackageVersion };

    private static async Task WriteImagesAsync(string output, IReadOnlyList<EncodedImage> images, CancellationToken cancellationToken)
    {
        var folder = Directory.CreateDirectory(Path.Combine(output, ImageEncoder.Folder)).FullName;
        foreach (var image in images)
            await File.WriteAllBytesAsync(Path.Combine(folder, image.File), image.Content.ToArray(), cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(folder, ImageList.FileName), ArtifactJson.Serialize(new ImageList(images)), cancellationToken).ConfigureAwait(false);
    }
}
