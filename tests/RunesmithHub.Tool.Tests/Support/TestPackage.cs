using System.Text;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>
/// Builds a package from C# source for analysis tests, compiling the contracts and implementation in memory, and the source and NuGet packages
/// the analysis checks it against.
/// </summary>
internal sealed record TestPackage
{
    public const string Repository = "https://github.com/lumen/todo";

    public static readonly string Commit = new('a', 40);

    public string Id { get; init; } = "lumen.todo";

    public string Name { get; init; } = "Lumen Todo";

    public string Version { get; init; } = "1.0.0";

    public string Root { get; init; } = "Lumen.Todo";

    public string? ContractsName { get; init; }

    public string License { get; init; } = "MIT";

    public string RunesmithApi { get; init; } = "^0.1.0";

    public string ContractsSource { get; init; } = "namespace Lumen.Todo.Contracts { public interface ITodoService { int Count { get; } } }";

    public string ImplementationSource { get; init; } =
        "namespace Lumen.Todo { public sealed class TodoService : Lumen.Todo.Contracts.ITodoService { public int Count => 0; } }";

    public IReadOnlyList<(string Id, string Reason)> Capabilities { get; init; } = [];

    public IReadOnlyList<string> NetworkHosts { get; init; } = [];

    public IReadOnlyList<(string Id, string Range)> Dependencies { get; init; } = [];

    /// <summary>Gets assemblies other than the contracts and implementation, by name, that the implementation is compiled against.</summary>
    public IReadOnlyList<(string Name, byte[] Image, string? Package, string? Version)> Bundled { get; init; } = [];

    /// <summary>Gets assemblies the contracts and implementation are compiled against but that the package does not carry.</summary>
    public IReadOnlyList<byte[]> References { get; init; } = [];

    /// <summary>Gets the NuGet packages the lock files pin with the license each states; they hold the bundled assemblies that name them.</summary>
    public IReadOnlyList<(string Id, string Version, string? License)> Locked { get; init; } = [];

    /// <summary>Gets native libraries by package path, such as <c>lib/native/linux-x64/libx.so</c>, and the locked package that holds each.</summary>
    public IReadOnlyList<(string Path, byte[] Content, string Package)> Native { get; init; } = [];

    /// <summary>Gets content files by path under <c>lib/</c>, such as <c>BuildHost/Host.exe</c>, and the locked package whose content files hold each.</summary>
    public IReadOnlyList<(string Path, byte[] Content, string Package)> Content { get; init; } = [];

    /// <summary>Gets what a NuGet package holds instead of a content file, by path under <c>lib/</c>.</summary>
    public IReadOnlyDictionary<string, byte[]> PublishedContent { get; init; } = new Dictionary<string, byte[]>();

    /// <summary>Gets what a NuGet package holds instead of a bundled assembly, by assembly name, as when a build changed the file.</summary>
    public IReadOnlyDictionary<string, byte[]> PublishedImages { get; init; } = new Dictionary<string, byte[]>();

    /// <summary>Gets the SBOM the package carries instead of the one its contents give.</summary>
    public byte[]? Sbom { get; init; }

    public IReadOnlyList<PackageFile> ExtraFiles { get; init; } = [];

    public IReadOnlyList<string>? Languages { get; init; }

    public ManifestLanguagePack? LanguagePack { get; init; }

    /// <summary>Gets the resources the implementation embeds, by their logical names, such as <c>locales/nl/lumen.todo.ftl</c>.</summary>
    public IReadOnlyList<(string Name, byte[] Content)> Resources { get; init; } = [];

    private string ContractsAssembly => ContractsName ?? Root + ".Contracts";

    public byte[] Contracts() => Compiler.Compile(ContractsAssembly, ContractsSource, [.. References]);

    public byte[] Implementation() =>
        Compiler.Compile(Root, ImplementationSource, unsafeCode: false, Resources, [Contracts(), .. References, .. Bundled.Select(bundled => bundled.Image)]);

    /// <summary>Gets the NuGet package file of every locked package.</summary>
    public IReadOnlyDictionary<(string Id, string Version), byte[]> NuGetPackages() => Locked.ToDictionary(
        package => (package.Id, package.Version),
        package => TestNuGet.Package(package.Id, package.Version, package.License,
        [
            .. Bundled.Where(bundled => bundled.Package == package.Id && bundled.Version == package.Version)
                .Select(bundled => ($"lib/net10.0/{bundled.Name}.dll", PublishedImages.GetValueOrDefault(bundled.Name) ?? bundled.Image)),
            .. Native.Where(native => native.Package == package.Id)
                .Select(native => ("runtimes/" + native.Path["lib/native/".Length..].Replace("/", "/native/", StringComparison.Ordinal), native.Content)),
            .. Content.Where(content => content.Package == package.Id)
                .Select(content => ($"contentFiles/any/any/{content.Path}", PublishedContent.GetValueOrDefault(content.Path) ?? content.Content)),
        ]));

    /// <summary>Gets a package source with <see cref="NuGetPackages"/>.</summary>
    public PackageCache PackageSource() => new(NuGetPackages());

    /// <summary>Writes the plugin's source as the analysis reads it at the release commit: its manifest and lock file.</summary>
    public AnalysisSource WriteSource(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, Protocol.Manifests.DeveloperManifest.FileName), RegistryJson.Serialize(DeveloperManifest()));
        TestNuGet.WriteLockFile(Path.Combine(directory, "a", LockFile.FileName), LockedPackages().Select(package => (package.Id, package.Version, package.ContentHash)));
        return new AnalysisSource(directory, Commit);
    }

    public string Write(string path)
    {
        var contracts = Contracts();
        var implementation = Implementation();
        var assemblies = new PackagedAssemblies
        {
            Contracts = new AssemblyFile { File = ContractsAssembly + ".dll", Sha256 = Hashes.Sha256(contracts) },
            Implementation = new AssemblyFile { File = Root + ".dll", Sha256 = Hashes.Sha256(implementation) },
            Bundled = [.. Bundled.Select(bundled => new BundledAssembly { File = bundled.Name + ".dll", Sha256 = Hashes.Sha256(bundled.Image), Package = bundled.Package, PackageVersion = bundled.Version }),
                .. Content.Select(content => new BundledAssembly { File = content.Path, Sha256 = Hashes.Sha256(content.Content), Package = content.Package, PackageVersion = Locked.First(locked => locked.Id == content.Package).Version })],
        };
        var manifest = PackagedManifest.Create(DeveloperManifest(), assemblies, new PackageBuild { Commit = Commit, Run = "local", Sdk = "10.0.400" });
        var locked = LockedPackages();
        var bundledPackages = BundledPackages(locked);
        List<PackageFile> files =
        [
            new(Protocol.Manifests.DeveloperManifest.FileName, IndexJson.Serialize(manifest)),
            new($"lib/{assemblies.Contracts.File}", contracts),
            new($"lib/{assemblies.Implementation.File}", implementation),
            .. Bundled.Select(bundled => new PackageFile($"lib/{bundled.Name}.dll", bundled.Image)),
            .. Native.Select(native => new PackageFile(native.Path, native.Content)),
            .. Content.Select(content => new PackageFile($"lib/{content.Path}", content.Content)),
            new("LICENSE", "MIT License\n"u8.ToArray()),
            new("README.md", "# Lumen Todo\n"u8.ToArray()),
            new(CycloneDxSbom.FileName, Sbom ?? CycloneDxSbom.Create(Id, Version, Commit, Repository, locked, bundledPackages)),
            new(ThirdPartyNotices.FileName, Encoding.UTF8.GetBytes(ThirdPartyNotices.Create(Name, bundledPackages))),
            .. ExtraFiles,
        ];
        File.WriteAllBytes(path, PackageWriter.Write(files));
        return path;
    }

    private List<LockedPackage> LockedPackages()
    {
        var packages = NuGetPackages();
        return [.. Locked.Select(package => new LockedPackage(package.Id, package.Version, TestNuGet.ContentHash(packages[(package.Id, package.Version)])))];
    }

    private List<BundledPackage> BundledPackages(List<LockedPackage> locked)
    {
        var packages = NuGetPackages();
        return [.. locked
            .Where(pinned => Bundled.Any(bundled => bundled.Package == pinned.Id && bundled.Version == pinned.Version) || Native.Any(native => native.Package == pinned.Id) || Content.Any(content => content.Package == pinned.Id))
            .OrderBy(pinned => pinned.Id, StringComparer.OrdinalIgnoreCase)
            .Select(pinned =>
            {
                using var package = NuGetPackage.Open(packages[(pinned.Id, pinned.Version)]);
                return BundledPackage.From(pinned, package);
            })];
    }

    private DeveloperManifest DeveloperManifest() => new()
    {
        SchemaVersion = 1,
        Id = Id,
        Name = Name,
        Version = Version,
        Summary = "Shows the todos of a project.",
        Authors = ["Lumen"],
        License = License,
        Repository = Repository,
        RunesmithApi = RunesmithApi,
        Projects = new ManifestProjects { Contracts = "a/A.Contracts.csproj", Implementation = "a/A.csproj" },
        Dependencies = [.. Dependencies.Select(dependency => new ManifestDependency { Id = dependency.Id, Range = dependency.Range })],
        Capabilities = [.. Capabilities.Select(capability => new ManifestCapability { Id = capability.Id, Reason = capability.Reason })],
        NetworkHosts = NetworkHosts,
        Icon = "icon.png",
        Categories = ["Productivity"],
        Languages = Languages,
        LanguagePack = LanguagePack,
    };
}
