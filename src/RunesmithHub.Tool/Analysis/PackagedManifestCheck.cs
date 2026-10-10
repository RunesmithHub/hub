using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Registry;

namespace RunesmithHub.Tool.Analysis;

/// <summary>
/// Checks the packaged manifest as untrusted data: every developer manifest rule, the registration's ID and repository, and normalised ranges.
/// </summary>
internal static class PackagedManifestCheck
{
    public static IEnumerable<AnalysisProblem> Check(PackagedManifest manifest, Registration registration)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(registration);
        var options = ManifestValidationOptions.Default with { ExpectedId = registration.Id, ExpectedRepository = registration.Repository.Url };
        foreach (var problem in ManifestValidator.Validate(AsDeveloperManifest(manifest), options))
            yield return AnalysisProblem.Error(ProblemCodes.Manifest, $"plugin.json: {problem}");

        if (!manifest.RunesmithApi.IsBounded || !manifest.RunesmithApi.StaysWithinOneLine)
            yield return AnalysisProblem.Error(ProblemCodes.Manifest, "plugin.json: runesmithApi is not normalised to one compatibility line.");
        foreach (var dependency in manifest.Dependencies.Where(dependency => !dependency.Range.IsBounded))
            yield return AnalysisProblem.Error(ProblemCodes.Manifest, $"plugin.json: the range of {dependency.Id} is not normalised.");
    }

    // The packaged manifest drops the project and image paths, so valid placeholders stand in for them.
    private static DeveloperManifest AsDeveloperManifest(PackagedManifest manifest) => new()
    {
        SchemaVersion = manifest.SchemaVersion,
        Id = manifest.Id,
        Name = manifest.Name,
        Version = manifest.Version.ToString(),
        Summary = manifest.Summary,
        Authors = manifest.Authors,
        License = manifest.License,
        Repository = manifest.Repository,
        Homepage = manifest.Homepage,
        RunesmithApi = manifest.RunesmithApi.ToString(),
        Projects = new ManifestProjects { Contracts = "Contracts.csproj", Implementation = "Implementation.csproj" },
        Dependencies = [.. manifest.Dependencies.Select(dependency => new ManifestDependency { Id = dependency.Id, Range = dependency.Range.ToString() })],
        Capabilities = [.. manifest.Capabilities.Select(capability => new ManifestCapability { Id = capability.Id, Reason = capability.Reason })],
        NetworkHosts = manifest.NetworkHosts,
        Platforms = manifest.Platforms,
        Icon = "icon.png",
        Categories = manifest.Categories,
        Keywords = manifest.Keywords,
        Deprecated = manifest.Deprecated is { } deprecated ? new ManifestDeprecation { Reason = deprecated.Reason, Replacement = deprecated.Replacement } : null,
        Languages = manifest.Languages,
        LanguagePack = manifest.LanguagePack is { } pack ? new ManifestLanguagePack { Language = pack.Language, Name = pack.Name, EnglishName = pack.EnglishName } : null,
    };
}
