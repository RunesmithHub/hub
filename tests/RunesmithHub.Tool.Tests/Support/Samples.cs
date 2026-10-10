using System.IO.Compression;
using System.Text;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Fixture;
using RunesmithHub.Tool.Images;
using RunesmithHub.Tool.Json;
using RunesmithHub.Tool.Registry;
using RunesmithHub.Tool.Versions;
using SkiaSharp;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>Records, analyses and build outputs for one sample plugin, <c>lumen.todo</c>, by Lumen Labs.</summary>
internal static class Samples
{
    public const string PluginId = "lumen.todo";
    public const string RepositoryName = "lumen-labs/lumen-todo";
    public const long RepositoryId = 7001;
    public const long OwnerId = 3001;
    public static readonly GitHubAccount Maintainer = new() { Id = 5001, Login = "ada" };
    public static readonly DateTimeOffset Time = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public static PublisherFile Publisher(string name = "lumen", long ownerId = OwnerId, string login = "lumen-labs") => new()
    {
        Name = name,
        DisplayName = "Lumen Labs",
        Owner = new PublisherOwner { Id = ownerId, Login = login, Type = OwnerType.Organization },
        State = PublisherState.Active,
    };

    public static Protocol.Registry.Registration Registration(
        string id = PluginId, long repositoryId = RepositoryId, string repository = RepositoryName, PluginTier tier = PluginTier.Unverified,
        RegistrationState state = RegistrationState.Listed, params GitHubAccount[] maintainers) => new()
        {
            Id = id,
            Repository = new RegisteredRepository { Id = repositoryId, Url = $"https://github.com/{repository}" },
            Maintainers = maintainers.Length == 0 ? [Maintainer] : maintainers,
            Tier = tier,
            State = state,
            RegisteredAt = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
        };

    /// <summary>Writes the analysis of a build output as the hub would: a version with no earlier record in the registry is a first version.</summary>
    public static byte[] AnalysisFor(RegistryState registry, string outputDirectory, string version)
    {
        var first = !registry.VersionsOf(PluginId).Any(record => record.Version < SemanticVersion.Parse(version));
        return first ? Analysis(outputDirectory, version, AnalysisOutcome.Review, [ReviewTriggers.FirstVersion]) : Analysis(outputDirectory, version);
    }

    /// <summary>Writes the <c>analysis.json</c> of a build output written by <see cref="WriteBuildOutput"/>, analysed at 11:00.</summary>
    public static byte[] Analysis(
        string outputDirectory, string version, AnalysisOutcome verdict = AnalysisOutcome.Passed, IReadOnlyList<string>? triggers = null,
        IReadOnlyList<AnalysisProblem>? problems = null)
    {
        var package = File.ReadAllBytes(Path.Combine(outputDirectory, $"{PluginId}-{version}.rsplugin"));
        var sbom = File.ReadAllBytes(Path.Combine(outputDirectory, $"{PluginId}-{version}.cdx.json"));
        return ArtifactJson.Serialize(new AnalysisReport
        {
            Id = PluginId,
            AnalyzedAt = Time.AddHours(-1),
            Verdict = verdict,
            Triggers = triggers ?? [],
            Problems = problems ?? [],
            Package = new AnalysedPackage(SemanticVersion.Parse(version), Hashes.Sha256(package), package.Length),
            Sbom = new ProducedFile(Hashes.Sha256(sbom), sbom.Length),
            Capabilities = new CapabilityAnalysis([], [new DetectedCapability("network", [], 0)], [], []),
        });
    }

    public static BuildJob Job(string version, int trackingIssue = 501, GitHubAccount? author = null) => new()
    {
        Plugin = PluginId,
        Version = version,
        Tag = "v" + version,
        Commit = Hashes.Sha256(Encoding.UTF8.GetBytes(version))[..40],
        Repository = RepositoryName,
        RepositoryId = RepositoryId,
        Path = ".",
        ReleaseAuthor = author ?? Maintainer,
        TrackingIssue = trackingIssue,
        ReleaseNotes = $"Release {version}.",
    };

    /// <summary>Writes a build output folder for a version, as <c>rshub build package</c> would.</summary>
    public static string WriteBuildOutput(string directory, string version, string name = "Lumen Todo", bool banner = false)
    {
        Directory.CreateDirectory(Path.Combine(directory, "images"));
        var images = new List<EncodedImage>();
        var icons = new Dictionary<int, byte[]>();
        foreach (var size in new[] { 32, 64, 128, 256 })
        {
            icons[size] = IconRenderer.RenderPng("#7c3aed", 'L', size);
            images.Add(new EncodedImage("icon", null, null, size, size, "png", $"icon-{size}.png"));
            File.WriteAllBytes(Path.Combine(directory, "images", $"icon-{size}.png"), icons[size]);
        }

        if (banner)
        {
            images.Add(new EncodedImage("banner", null, null, 1280, 640, "webp", "banner-1280.webp"));
            images.Add(new EncodedImage("banner", null, null, 640, 320, "webp", "banner-640.webp"));
            File.WriteAllBytes(Path.Combine(directory, "images", "banner-640.webp"), Webp(640, 320));
            File.WriteAllBytes(Path.Combine(directory, "images", "banner-1280.webp"), Webp(1280, 640));
        }

        File.WriteAllBytes(Path.Combine(directory, "images", ImageList.FileName), ArtifactJson.Serialize(new ImageList(images)));

        var contracts = Encoding.UTF8.GetBytes($"contracts {version}");
        var implementation = Encoding.UTF8.GetBytes($"implementation {version}");
        var developer = new DeveloperManifest
        {
            SchemaVersion = 1,
            Id = PluginId,
            Name = name,
            Version = version,
            Summary = "Collects TODO comments.",
            Authors = ["Lumen Labs"],
            License = "MIT",
            Repository = $"https://github.com/{RepositoryName}",
            RunesmithApi = "^0.1.0",
            Projects = new ManifestProjects { Contracts = "src/C/C.csproj", Implementation = "src/I/I.csproj" },
            Capabilities = [new ManifestCapability { Id = "network", Reason = "Links TODOs to issues." }],
            NetworkHosts = ["api.github.com"],
            Icon = "icon.png",
            Categories = ["Productivity"],
        };
        var assemblies = new PackagedAssemblies
        {
            Contracts = new AssemblyFile { File = "Lumen.Todo.Contracts.dll", Sha256 = Hashes.Sha256(contracts) },
            Implementation = new AssemblyFile { File = "Lumen.Todo.dll", Sha256 = Hashes.Sha256(implementation) },
        };
        var manifest = PackagedManifest.Create(developer, assemblies, new PackageBuild { Commit = Job(version).Commit, Run = "local", Sdk = "10.0.400" });
        var package = PackageWriter.Write(
        [
            new(DeveloperManifest.FileName, Protocol.Json.IndexJson.Serialize(manifest)),
            new("lib/Lumen.Todo.Contracts.dll", contracts),
            new("lib/Lumen.Todo.dll", implementation),
            new("icon/64.png", icons[64]),
            new("icon/128.png", icons[128]),
        ]);
        File.WriteAllBytes(Path.Combine(directory, $"{PluginId}-{version}.rsplugin"), package);
        File.WriteAllBytes(Path.Combine(directory, $"RunesmithHub.Lumen.Todo.Contracts.{version}.nupkg"), ContractsPackage(version, contracts));
        File.WriteAllBytes(Path.Combine(directory, $"{PluginId}-{version}.source.tar.gz"), Encoding.UTF8.GetBytes($"source {version}"));
        File.WriteAllText(Path.Combine(directory, $"{PluginId}-{version}.cdx.json"),
            """{ "bomFormat": "CycloneDX", "components": [ { "name": "Humanizer.Core", "version": "2.14.1", "purl": "pkg:nuget/Humanizer.Core@2.14.1" } ] }""");
        return Rehash(directory);
    }

    /// <summary>Writes the <c>hashes.json</c> of a build output again, after a test changed its files.</summary>
    public static string Rehash(string directory)
    {
        HashList.Create(directory).Write(directory);
        return directory;
    }

    /// <summary>Computes the record the pipeline would propose for a build output written by <see cref="WriteBuildOutput"/>.</summary>
    public static async Task<RegistryVersionRecord> RecordAsync(RegistryState registry, string outputDirectory, string version, long runId = 18_000_001, byte[]? analysis = null)
    {
        var inputs = VersionInputs.Read(analysis ?? AnalysisFor(registry, outputDirectory, version), Job(version), outputDirectory, runId);
        return (await VersionRecordFactory.CreateAsync(inputs, registry, new NoHistory(), TestContext.Current.CancellationToken)).Record;
    }

    private static byte[] ContractsPackage(string version, byte[] contracts)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, byte[] content)
            {
                using var stream = archive.CreateEntry(path).Open();
                stream.Write(content);
            }

            Add("RunesmithHub.Lumen.Todo.Contracts.nuspec", Encoding.UTF8.GetBytes($"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>RunesmithHub.Lumen.Todo.Contracts</id><version>{version}</version></metadata></package>
                """));
            Add("lib/net10.0/Lumen.Todo.Contracts.dll", contracts);
        }

        return buffer.ToArray();
    }

    private static byte[] Webp(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Purple);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 80);
        return data.ToArray();
    }

    /// <summary>A history that knows nothing.</summary>
    internal sealed class NoHistory : IRegistryHistory
    {
        public Task<IReadOnlySet<long>?> MaintainersAtVersionAsync(string pluginId, SemanticVersion version, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<long>?>(null);
    }

    /// <summary>A history with fixed maintainers for every earlier version.</summary>
    internal sealed class FixedHistory(params long[] maintainers) : IRegistryHistory
    {
        public Task<IReadOnlySet<long>?> MaintainersAtVersionAsync(string pluginId, SemanticVersion version, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<long>?>(maintainers.ToHashSet());
    }
}
