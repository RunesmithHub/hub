using System.Text;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Packaging;

namespace RunesmithHub.Tool.Fixture;

/// <summary>Builds the developer manifest and a package with placeholder assemblies for a sample version.</summary>
internal static class SamplePackage
{
    public const string Sdk = "10.0.401";

    public static string Commit(SamplePlugin plugin, SampleVersion version) => Hashes.Sha256(Encoding.UTF8.GetBytes($"{plugin.Id}@{version.Version}"))[..40];

    public static string ContractsFile(SamplePlugin plugin) => $"{plugin.AssemblyRoot}.Contracts.dll";

    public static byte[] ContractsAssembly(SamplePlugin plugin, SampleVersion version) =>
        Encoding.UTF8.GetBytes($"Placeholder contracts assembly {plugin.AssemblyRoot}.Contracts {version.Version}\n");

    private static byte[] ImplementationAssembly(SamplePlugin plugin, SampleVersion version) =>
        Encoding.UTF8.GetBytes($"Placeholder implementation assembly {plugin.AssemblyRoot} {version.Version}\n");

    public static DeveloperManifest Manifest(SamplePlugin plugin, SampleVersion version, string author) => new()
    {
        SchemaVersion = DeveloperManifest.CurrentSchemaVersion,
        Id = plugin.Id,
        Name = plugin.Name,
        Version = version.Version,
        Summary = plugin.Summary,
        Authors = [author],
        License = plugin.License,
        Repository = plugin.Repository.Url,
        RunesmithApi = SampleData.RunesmithApi,
        Projects = new ManifestProjects
        {
            Contracts = $"src/{plugin.AssemblyRoot}.Contracts/{plugin.AssemblyRoot}.Contracts.csproj",
            Implementation = $"src/{plugin.AssemblyRoot}/{plugin.AssemblyRoot}.csproj",
        },
        Dependencies = plugin.Dependencies.Count == 0 ? null : [.. plugin.Dependencies.Select(d => new ManifestDependency { Id = d.Id, Range = d.Range })],
        Capabilities = plugin.Capabilities.Count == 0 ? null : [.. plugin.Capabilities.Select(c => new ManifestCapability { Id = c.Id, Reason = c.Reason })],
        NetworkHosts = plugin.NetworkHosts.Count == 0 ? null : plugin.NetworkHosts,
        Icon = "icon.png",
        Categories = plugin.Categories,
        Keywords = plugin.Keywords.Count == 0 ? null : plugin.Keywords,
        Deprecated = plugin.Deprecated is { } deprecated ? new ManifestDeprecation { Reason = deprecated.Reason, Replacement = deprecated.Replacement } : null,
        Languages = plugin.Languages,
        LanguagePack = plugin.LanguagePack is { } pack ? new ManifestLanguagePack { Language = pack.Language, Name = pack.Name, EnglishName = pack.EnglishName } : null,
    };

    /// <summary>Builds the package: the packaged manifest, both assemblies, the icons, the license and the readme.</summary>
    public static byte[] Build(SamplePlugin plugin, SampleVersion version, string author, string buildRun, byte[] icon64, byte[] icon128)
    {
        var contracts = ContractsAssembly(plugin, version);
        var implementation = ImplementationAssembly(plugin, version);
        var assemblies = new PackagedAssemblies
        {
            Contracts = new AssemblyFile { File = ContractsFile(plugin), Sha256 = Hashes.Sha256(contracts) },
            Implementation = new AssemblyFile { File = $"{plugin.AssemblyRoot}.dll", Sha256 = Hashes.Sha256(implementation) },
        };
        var manifest = PackagedManifest.Create(Manifest(plugin, version, author), assemblies, new PackageBuild { Commit = Commit(plugin, version), Run = buildRun, Sdk = Sdk });

        return PackageWriter.Write(
        [
            new(DeveloperManifest.FileName, Protocol.Json.IndexJson.Serialize(manifest)),
            new($"lib/{assemblies.Contracts.File}", contracts),
            new($"lib/{assemblies.Implementation.File}", implementation),
            new("icon/64.png", icon64),
            new("icon/128.png", icon128),
            new("LICENSE", Encoding.UTF8.GetBytes($"{plugin.Name} is licensed under {plugin.License}. This sample package holds placeholder text only.\n")),
            new("README.md", Encoding.UTF8.GetBytes($"# {plugin.Name}\n\n{plugin.Summary}\n")),
        ]);
    }
}
