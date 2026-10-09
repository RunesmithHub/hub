using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Images;
using RunesmithHub.Tool.Json;
using SkiaSharp;

namespace RunesmithHub.Tool.Versions;

/// <summary>A file of the build output with its hash and size.</summary>
internal sealed record OutputFile(string Name, string Path, string Sha256, long Length)
{
    public static OutputFile Of(string root, string path)
    {
        using var stream = File.OpenRead(path);
        return new OutputFile(System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'), path, Hashes.Sha256(stream), stream.Length);
    }

    public string AssetName => System.IO.Path.GetFileName(Name);

    public HostedFile Hosted(string url) => new() { Urls = [url], Sha256 = Sha256, Length = Length };

    public AssetFile Asset() => new() { File = AssetName, Sha256 = Sha256, Length = Length };
}

/// <summary>An image of the build output with its dimensions, as read from the file itself.</summary>
internal sealed record OutputImage(OutputFile File, int Width, int Height, ImageFormat Format)
{
    public string AssetName => File.AssetName;

    public HostedImage Hosted(string url) => new()
    {
        Url = url,
        Sha256 = File.Sha256,
        Length = File.Length,
        Width = Width,
        Height = Height,
        Format = Format,
    };
}

/// <summary>A screenshot at both widths with its caption.</summary>
internal sealed record OutputGalleryItem(string Caption, OutputImage Width640, OutputImage Width1280);

/// <summary>
/// The output of <c>rshub build package</c> for one version, read as untrusted data: files are found by the names the build writes, the
/// package must pass every packaging rule, and the contracts package must hold the package's contracts assembly.
/// </summary>
internal sealed class BuildOutput
{
    private static readonly int[] IconSizes = [64, 128, 256];

    private BuildOutput(string directory, string pluginId, SemanticVersion version, PackageLimits limits)
    {
        Directory = System.IO.Directory.Exists(directory) ? Path.GetFullPath(directory) : throw new ToolException($"{directory} does not exist.");
        var text = version.ToString();
        Package = File(BuildArtifacts.Package(pluginId, text));
        Source = File(BuildArtifacts.Source(pluginId, text));
        Sbom = File(BuildArtifacts.Sbom(pluginId, text));
        Manifest = ReadManifest(limits);
        if (!string.Equals(Manifest.Id, pluginId, StringComparison.Ordinal) || Manifest.Version != version)
            throw new ToolException($"The package is {Manifest.Id} {Manifest.Version}, not {pluginId} {version}.");

        ContractsPackageId = ContractsNuGetPackage.Id(Manifest.Assemblies.Contracts.File);
        ContractsPackage = File(BuildArtifacts.ContractsPackage(Manifest.Assemblies.Contracts.File, text));
        var problems = ContractsNuGetPackage.Check(System.IO.File.ReadAllBytes(ContractsPackage.Path), Manifest, limits.MaxUnpackedBytes);
        if (problems.Count > 0)
            throw new ToolException($"{ContractsPackage.Name}: {string.Join("; ", problems)}.");

        var images = ReadImages();
        Icons = [.. IconSizes.Select(size => Find(images, "icon", null, size) ?? throw new ToolException($"{ImageEncoder.Folder}/{ImageList.FileName} lists no {size} pixel icon."))];
        Banner = Find(images, "banner", null, 640) is { } banner640 && Find(images, "banner", null, 1280) is { } banner1280 ? (banner640, banner1280) : null;
        Gallery = [.. images.Where(image => image.Listed.Kind == "gallery").Select(image => image.Listed.Index ?? 0).Distinct().Order().Select(index =>
            new OutputGalleryItem(
                images.First(image => image.Listed.Kind == "gallery" && image.Listed.Index == index).Listed.Caption ?? "",
                Find(images, "gallery", index, 640) ?? throw new ToolException($"Gallery image {index} has no 640 pixel version."),
                Find(images, "gallery", index, 1280) ?? throw new ToolException($"Gallery image {index} has no 1280 pixel version.")))];
    }

    public string Directory { get; }

    public OutputFile Package { get; }

    /// <summary>Gets the package's manifest, read with the packaging rules.</summary>
    public PackagedManifest Manifest { get; }

    public OutputFile ContractsPackage { get; }

    /// <summary>Gets the contracts package's NuGet ID, such as <c>RunesmithHub.Lumen.Todo.Contracts</c>.</summary>
    public string ContractsPackageId { get; }

    public OutputFile Source { get; }

    public OutputFile Sbom { get; }

    /// <summary>Gets the icons at 64, 128 and 256 pixels.</summary>
    public IReadOnlyList<OutputImage> Icons { get; }

    public (OutputImage Width640, OutputImage Width1280)? Banner { get; }

    public IReadOnlyList<OutputGalleryItem> Gallery { get; }

    /// <summary>Gets every file the release carries: the package, the source archive, the SBOM and the hosted images.</summary>
    public IEnumerable<OutputFile> ReleaseFiles =>
        new[] { Package, Source, Sbom }
            .Concat(Icons.Select(icon => icon.File))
            .Concat(Banner is { } banner ? [banner.Width640.File, banner.Width1280.File] : [])
            .Concat(Gallery.SelectMany(item => new[] { item.Width640.File, item.Width1280.File }));

    /// <summary>Opens the output of a version's build.</summary>
    /// <exception cref="ToolException">A file is missing, breaks a rule, or belongs to another version.</exception>
    public static BuildOutput Open(string directory, string pluginId, SemanticVersion version, PackageLimits limits)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(limits);
        return new BuildOutput(directory, pluginId, version, limits);
    }

    private OutputFile File(string name)
    {
        var path = Path.Combine(Directory, name);
        return System.IO.File.Exists(path) ? OutputFile.Of(Directory, path) : throw new ToolException($"{Directory} has no {name}.");
    }

    private PackagedManifest ReadManifest(PackageLimits limits)
    {
        try
        {
            using var stream = System.IO.File.OpenRead(Package.Path);
            using var archive = PackageArchive.Open(stream, limits);
            return archive.ReadManifest();
        }
        catch (Exception exception) when (exception is PackageException or InvalidDataException)
        {
            throw new ToolException($"{Package.Name}: {exception.Message}", exception);
        }
    }

    private List<(EncodedImage Listed, OutputImage Image)> ReadImages()
    {
        var folder = Path.Combine(Directory, ImageEncoder.Folder);
        var listPath = Path.Combine(folder, ImageList.FileName);
        if (!System.IO.File.Exists(listPath))
            throw new ToolException($"{Directory} has no {ImageEncoder.Folder}/{ImageList.FileName}.");

        ImageList list;
        try
        {
            list = ArtifactJson.Deserialize<ImageList>(System.IO.File.ReadAllBytes(listPath));
        }
        catch (InvalidDataException exception)
        {
            throw new ToolException($"{ImageEncoder.Folder}/{ImageList.FileName}: {exception.Message}", exception);
        }

        return [.. list.Images.Where(image => image.Kind != "icon" || IconSizes.Contains(image.Width)).Select(listed => (listed, Measure(folder, listed)))];
    }

    private OutputImage Measure(string folder, EncodedImage listed)
    {
        if (listed.File.Contains('/', StringComparison.Ordinal) || listed.File.Contains('\\', StringComparison.Ordinal) || listed.File.StartsWith('.'))
            throw new ToolException($"{ImageEncoder.Folder}/{ImageList.FileName} names the file \"{listed.File}\".");
        var path = Path.Combine(folder, listed.File);
        if (!System.IO.File.Exists(path))
            throw new ToolException($"{ImageEncoder.Folder}/{listed.File} is missing.");

        using var codec = SKCodec.Create(path) ?? throw new ToolException($"{ImageEncoder.Folder}/{listed.File} is not an image.");
        var format = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Png => ImageFormat.Png,
            SKEncodedImageFormat.Webp => ImageFormat.Webp,
            _ => throw new ToolException($"{ImageEncoder.Folder}/{listed.File} is neither PNG nor WebP."),
        };
        var expected = listed.Kind == "icon" ? ImageFormat.Png : ImageFormat.Webp;
        if (format != expected || codec.Info.Width != listed.Width || codec.Info.Height != listed.Height)
            throw new ToolException($"{ImageEncoder.Folder}/{listed.File} is not the {listed.Width}x{listed.Height} image {ImageList.FileName} lists.");
        return new OutputImage(OutputFile.Of(Directory, path), codec.Info.Width, codec.Info.Height, format);
    }

    private static OutputImage? Find(List<(EncodedImage Listed, OutputImage Image)> images, string kind, int? index, int width) =>
        images.FirstOrDefault(image => image.Listed.Kind == kind && image.Listed.Index == index && image.Listed.Width == width).Image;
}
