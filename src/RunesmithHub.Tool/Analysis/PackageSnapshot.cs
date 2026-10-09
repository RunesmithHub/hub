using Mono.Cecil;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Tool.Build;

namespace RunesmithHub.Tool.Analysis;

/// <summary>
/// A package opened for analysis: checked against the packaging rules, extracted into a temporary folder, its manifest read and its
/// assemblies read as metadata. Disposing it deletes the folder.
/// </summary>
internal sealed class PackageSnapshot : IDisposable
{
    private const string LibFolder = "lib/";

    private readonly string _staging;

    private PackageSnapshot(string staging, long length, string sha256, PackagedManifest manifest, IReadOnlyList<string> entries)
    {
        _staging = staging;
        Length = length;
        Sha256 = sha256;
        Manifest = manifest;
        Entries = entries;
    }

    public long Length { get; }

    public string Sha256 { get; }

    public PackagedManifest Manifest { get; }

    /// <summary>Gets every file's path in the package.</summary>
    public IReadOnlyList<string> Entries { get; }

    /// <summary>Gets the assemblies under <c>lib/</c> that could be read, contracts and implementation first.</summary>
    public IReadOnlyList<LoadedAssembly> Assemblies { get; private set; } = [];

    /// <summary>Gets the problems found while reading the assemblies and checking them against the manifest.</summary>
    public IReadOnlyList<AnalysisProblem> Problems { get; private set; } = [];

    public IReadOnlyList<SbomComponent> Components { get; private set; } = [];

    public LoadedAssembly? Contracts => Assemblies.FirstOrDefault(assembly => assembly.Role == AssemblyRole.Contracts);

    public LoadedAssembly? Implementation => Assemblies.FirstOrDefault(assembly => assembly.Role == AssemblyRole.Implementation);

    /// <summary>Gets the native libraries under <c>lib/native/</c>.</summary>
    public IEnumerable<string> NativeLibraries => Entries.Where(entry => entry.StartsWith(PackageEntryRules.NativePrefix, StringComparison.Ordinal));

    /// <exception cref="PackageException">The package breaks a packaging rule.</exception>
    public static PackageSnapshot Open(string path, PackageLimits limits)
    {
        var bytes = File.ReadAllBytes(path);
        var staging = Path.Combine(Path.GetTempPath(), $"rshub-analysis-{Guid.NewGuid():N}");
        try
        {
            using var archive = PackageArchive.Open(new MemoryStream(bytes, writable: false), limits);
            var manifest = archive.ReadManifest();
            archive.ExtractTo(staging);
            var snapshot = new PackageSnapshot(staging, bytes.Length, Hashes.Sha256(bytes), manifest, archive.EntryNames);
            snapshot.Load();
            return snapshot;
        }
        catch
        {
            Delete(staging);
            throw;
        }
    }

    /// <summary>Reads an extracted file by its path in the package.</summary>
    public byte[] ReadFile(string entry) => File.ReadAllBytes(Path.Combine(_staging, entry));

    private void Load()
    {
        var problems = PackageVerifier.CheckAssemblies(_staging, Manifest).Select(problem => AnalysisProblem.Error(ProblemCodes.PackageRules, problem)).ToList();
        var assemblies = new List<LoadedAssembly>();
        foreach (var entry in Entries.Where(IsAssembly).OrderBy(RoleOrder).ThenBy(entry => entry, StringComparer.Ordinal))
        {
            var file = entry[LibFolder.Length..];
            try
            {
                var definition = AssemblyDefinition.ReadAssembly(Path.Combine(_staging, entry), new ReaderParameters { ReadingMode = ReadingMode.Immediate, InMemory = true });
                assemblies.Add(new LoadedAssembly(file, RoleOf(file), definition));
            }
            catch (Exception exception) when (exception is BadImageFormatException or InvalidOperationException or ArgumentException or IOException)
            {
                problems.Add(AnalysisProblem.Error(ProblemCodes.Assembly, $"lib/{file} is not a .NET assembly the analysis can read.", $"lib/{file}"));
            }
        }

        if (Entries.Contains(CycloneDxSbom.FileName))
        {
            try
            {
                Components = SbomComponent.Read(ReadFile(CycloneDxSbom.FileName));
            }
            catch (InvalidDataException exception)
            {
                problems.Add(AnalysisProblem.Error(ProblemCodes.PackageRules, exception.Message, CycloneDxSbom.FileName));
            }
        }
        else
            problems.Add(AnalysisProblem.Error(ProblemCodes.PackageRules, $"The package has no {CycloneDxSbom.FileName}."));

        Assemblies = assemblies;
        Problems = problems;
    }

    private static bool IsAssembly(string entry) =>
        entry.StartsWith(LibFolder, StringComparison.Ordinal) && !entry.StartsWith(PackageEntryRules.NativePrefix, StringComparison.Ordinal)
        && entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private int RoleOrder(string entry) => (int)RoleOf(entry[LibFolder.Length..]);

    private AssemblyRole RoleOf(string file) =>
        file == Manifest.Assemblies.Contracts.File ? AssemblyRole.Contracts
        : file == Manifest.Assemblies.Implementation.File ? AssemblyRole.Implementation
        : AssemblyRole.Bundled;

    private static void Delete(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    public void Dispose()
    {
        foreach (var assembly in Assemblies)
            assembly.Definition.Dispose();
        Delete(_staging);
    }
}
