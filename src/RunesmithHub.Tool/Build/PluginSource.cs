using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Build;

/// <summary>A plugin's folder in its checked out repository, with its manifest validated.</summary>
internal sealed record PluginSource(string Root, DeveloperManifest Manifest)
{
    public string Id => Manifest.Id!;

    public SemanticVersion Version => SemanticVersion.Parse(Manifest.Version!);

    /// <summary>Gets the normalised Runesmith API range.</summary>
    public VersionRange RunesmithApi => VersionRange.Parse(Manifest.RunesmithApi!).Normalize();

    public string ContractsProject => Resolve(Manifest.Projects!.Contracts!);

    public string ImplementationProject => Resolve(Manifest.Projects!.Implementation!);

    /// <summary>Gets the absolute path of a path relative to the plugin root.</summary>
    public string Resolve(string relativePath) => Path.GetFullPath(Path.Combine(Root, relativePath));

    /// <summary>Reads the packages both projects' lock files pin, each once.</summary>
    /// <exception cref="ToolException">A lock file is missing, is a symbolic link or is not valid.</exception>
    public IReadOnlyList<LockedPackage> LockedPackages() =>
        [.. new[] { ContractsProject, ImplementationProject }
            .Select(LockFile.PathFor)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(ReadLockFile)
            .DistinctBy(package => (package.Id.ToUpperInvariant(), package.Version))];

    /// <summary>Reads and validates the manifest in a plugin folder.</summary>
    /// <exception cref="ToolException">The manifest is missing, a symbolic link, or not valid; every problem is listed.</exception>
    public static PluginSource Load(string root, PluginExpectations expectations)
    {
        ArgumentNullException.ThrowIfNull(expectations);
        root = Path.GetFullPath(root);
        var path = Path.Combine(root, DeveloperManifest.FileName);
        if (!CheckoutPaths.FileExists(root, path))
            throw new ToolException($"{path} does not exist.");

        var options = ManifestValidationOptions.Default with
        {
            ExpectedId = expectations.Id,
            ExpectedRepository = expectations.Repository,
            ExpectedVersion = expectations.Version,
        };
        var result = ManifestValidator.Validate(CheckoutPaths.ReadFile(root, path), options);
        if (!result.IsValid)
            throw new ToolException($"{DeveloperManifest.FileName} is not valid:\n" + string.Join("\n", result.Problems.Select(problem => $"  {problem}")));
        return new PluginSource(root, result.Manifest);
    }

    private IReadOnlyList<LockedPackage> ReadLockFile(string path) =>
        CheckoutPaths.FileExists(Root, path) ? LockFile.Read(Root, path) : throw new ToolException($"{Path.GetRelativePath(Root, path).Replace('\\', '/')} is missing.");
}
