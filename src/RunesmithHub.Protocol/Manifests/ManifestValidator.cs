using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Manifests;

/// <summary>Reads and checks a developer's <c>plugin.json</c>, listing every problem with its field path.</summary>
public static partial class ManifestValidator
{
    private const int MaxDependencies = 32;
    private const int MaxGalleryImages = 6;
    private const int MaxKeywords = 10;
    private const int MaxLanguages = 100;

    /// <summary>Reads and checks a manifest file's bytes.</summary>
    public static ManifestValidationResult Validate(ReadOnlySpan<byte> json, ManifestValidationOptions? options = null)
    {
        DeveloperManifest manifest;
        try
        {
            manifest = RegistryJson.Deserialize<DeveloperManifest>(json);
        }
        catch (JsonException exception)
        {
            var path = exception.Path is { Length: > 1 } jsonPath ? jsonPath.TrimStart('$').TrimStart('.') : "";
            return new ManifestValidationResult(null, [new ManifestProblem(path, FirstSentence(exception.Message))]);
        }

        return new ManifestValidationResult(manifest, Validate(manifest, options ?? ManifestValidationOptions.Default));
    }

    /// <summary>Checks a manifest against the rules for every field.</summary>
    public static IReadOnlyList<ManifestProblem> Validate(DeveloperManifest manifest, ManifestValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var checker = new Checker(options ?? ManifestValidationOptions.Default);
        checker.Check(manifest);
        return checker.Problems;
    }

    private static string FirstSentence(string message)
    {
        var end = message.IndexOf(". ", StringComparison.Ordinal);
        return end < 0 ? message : message[..(end + 1)];
    }

    [GeneratedRegex("^https://github\\.com/[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex GitHubRepository();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,29}$")]
    private static partial Regex Keyword();

    private sealed class Checker(ManifestValidationOptions options)
    {
        public List<ManifestProblem> Problems { get; } = [];

        public void Check(DeveloperManifest manifest)
        {
            CheckIdentity(manifest);
            CheckDescription(manifest);
            CheckApi(manifest.RunesmithApi);
            CheckProjects(manifest.Projects);
            CheckDependencies(manifest.Dependencies, manifest.Id);
            CheckCapabilities(manifest.Capabilities, manifest.NetworkHosts);
            CheckPlatforms(manifest.Platforms);
            CheckImages(manifest);
            CheckCategories(manifest.Categories);
            CheckKeywords(manifest.Keywords);
            CheckDeprecation(manifest.Deprecated, manifest.Id);
            CheckLocalization(manifest);
        }

        private void CheckIdentity(DeveloperManifest manifest)
        {
            if (manifest.SchemaVersion is null)
                Add("schemaVersion", "is required.");
            else if (manifest.SchemaVersion != DeveloperManifest.CurrentSchemaVersion)
                Add("schemaVersion", $"must be {DeveloperManifest.CurrentSchemaVersion}.");

            if (Required("id", manifest.Id) is { } id)
            {
                if (!PluginId.IsValid(id))
                    Add("id", "must be <publisher>.<name>: lowercase letters, digits and single hyphens, each part 2 to 40 characters starting with a letter.");
                else if (options.ExpectedId is { } expectedId && !string.Equals(id, expectedId, StringComparison.Ordinal))
                    Add("id", $"must be \"{expectedId}\", the ID in the registration.");
            }

            if (Required("version", manifest.Version) is { } version)
            {
                if (!SemanticVersion.TryParse(version, out _))
                    Add("version", "must be a semantic version such as 1.4.2 or 1.5.0-beta.2, without build metadata.");
                else if (options.ExpectedVersion is { } expectedVersion && !string.Equals(version, expectedVersion, StringComparison.Ordinal))
                    Add("version", $"must be {expectedVersion}, the version of the release tag.");
            }

            if (Required("repository", manifest.Repository) is { } repository)
            {
                if (!GitHubRepository().IsMatch(repository))
                    Add("repository", "must be https://github.com/<owner>/<repo>.");
                else if (options.ExpectedRepository is { } expected && !string.Equals(repository, expected, StringComparison.OrdinalIgnoreCase))
                    Add("repository", $"must be {expected}, the repository in the registration.");
            }
        }

        private void CheckDescription(DeveloperManifest manifest)
        {
            if (Required("name", manifest.Name) is { } name)
                Length("name", name, 2, 40);

            if (Required("summary", manifest.Summary) is { } summary)
                Line("summary", summary, 1, 120);

            if (manifest.Authors is null)
                Add("authors", "is required.");
            else
            {
                Count("authors", manifest.Authors.Count, 1, 10);
                for (var i = 0; i < manifest.Authors.Count; i++)
                    Length(Index("authors", i), manifest.Authors[i], 1, 80);
            }

            if (Required("license", manifest.License) is { } license && SpdxExpression.Check(license, options.IsApprovedLicense) is { } licenseError)
                Add("license", licenseError);

            if (manifest.Homepage is { } homepage && (!Uri.TryCreate(homepage, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
                Add("homepage", "must be an https:// URL.");
        }

        private void CheckApi(string? api)
        {
            if (Required("runesmithApi", api) is not { } text)
                return;
            if (!VersionRange.TryParse(text, out var range, out var error))
                Add("runesmithApi", error);
            else if (!range.Normalize().StaysWithinOneLine)
                Add("runesmithApi", "must stay within one compatibility line, such as ^0.4.0; publish a version for each line instead.");
        }

        private void CheckProjects(ManifestProjects? projects)
        {
            if (projects is null)
            {
                Add("projects", "is required.");
                return;
            }

            ProjectPath("projects.contracts", projects.Contracts);
            ProjectPath("projects.implementation", projects.Implementation);
            if (projects.Contracts is not null && string.Equals(projects.Contracts, projects.Implementation, StringComparison.Ordinal))
                Add("projects.implementation", "must be a different project from the contracts project.");
        }

        private void ProjectPath(string path, string? value)
        {
            if (Required(path, value) is not { } text)
                return;
            if (!RelativePaths.IsSafe(text) || !text.EndsWith(".csproj", StringComparison.Ordinal))
                Add(path, "must be a relative path to a .csproj file, with forward slashes and no \"..\".");
        }

        private void CheckDependencies(IReadOnlyList<ManifestDependency>? dependencies, string? ownId)
        {
            if (dependencies is null)
                return;
            if (dependencies.Count > MaxDependencies)
                Add("dependencies", $"can have at most {MaxDependencies} items.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < dependencies.Count; i++)
            {
                var path = Index("dependencies", i);
                var dependency = dependencies[i];
                if (Required(path + ".id", dependency.Id) is { } id)
                {
                    if (!PluginId.IsValid(id))
                        Add(path + ".id", "must be a plugin ID.");
                    else if (string.Equals(id, ownId, StringComparison.Ordinal))
                        Add(path + ".id", "can't be the plugin itself.");
                    else if (!seen.Add(id))
                        Add(path + ".id", $"lists {id} again.");
                }

                if (Required(path + ".range", dependency.Range) is { } range && !VersionRange.TryParse(range, out _, out var error))
                    Add(path + ".range", error);
            }
        }

        private void CheckCapabilities(IReadOnlyList<ManifestCapability>? capabilities, IReadOnlyList<string>? hosts)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < (capabilities?.Count ?? 0); i++)
            {
                var path = Index("capabilities", i);
                var capability = capabilities![i];
                if (Required(path + ".id", capability.Id) is { } id)
                {
                    if (!Protocol.Capabilities.IsKnown(id))
                        Add(path + ".id", $"must be one of {string.Join(", ", Protocol.Capabilities.All)}.");
                    else if (!seen.Add(id))
                        Add(path + ".id", $"lists {id} again.");
                }

                if (Required(path + ".reason", capability.Reason) is { } reason)
                    Length(path + ".reason", reason, 1, 160);
            }

            if (seen.Contains(Protocol.Capabilities.Network) && (hosts is null || hosts.Count == 0))
                Add("networkHosts", "is required with the network capability; list the hosts, or \"*\" for hosts the user chooses.");
            else if (!seen.Contains(Protocol.Capabilities.Network) && hosts is { Count: > 0 })
                Add("networkHosts", "is only allowed with the network capability; declare it, or remove the hosts.");

            for (var i = 0; i < (hosts?.Count ?? 0); i++)
            {
                var host = hosts![i];
                if (host != "*" && Uri.CheckHostName(host) == UriHostNameType.Unknown)
                    Add(Index("networkHosts", i), "must be a host name, or \"*\" for hosts the user chooses.");
            }
        }

        private void CheckPlatforms(IReadOnlyList<string>? platforms)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < (platforms?.Count ?? 0); i++)
            {
                var platform = platforms![i];
                if (!Protocol.Platforms.IsKnown(platform))
                    Add(Index("platforms", i), $"must be one of {string.Join(", ", Protocol.Platforms.All)}.");
                else if (!seen.Add(platform))
                    Add(Index("platforms", i), $"lists {platform} again.");
            }
        }

        private void CheckImages(DeveloperManifest manifest)
        {
            if (Required("icon", manifest.Icon) is { } icon)
                ImagePath("icon", icon, ".png", ".webp");
            if (manifest.Banner is { } banner)
                ImagePath("banner", banner, ".png", ".webp", ".jpg", ".jpeg");

            if (manifest.Gallery is not { } gallery)
                return;
            if (gallery.Count > MaxGalleryImages)
                Add("gallery", $"can have at most {MaxGalleryImages} images.");
            for (var i = 0; i < gallery.Count; i++)
            {
                var path = Index("gallery", i);
                if (Required(path + ".path", gallery[i].Path) is { } imagePath)
                    ImagePath(path + ".path", imagePath, ".png", ".webp", ".jpg", ".jpeg");
                if (Required(path + ".caption", gallery[i].Caption) is { } caption)
                    Length(path + ".caption", caption, 1, 80);
            }
        }

        private void ImagePath(string path, string value, params string[] extensions)
        {
            if (!RelativePaths.IsSafe(value) || !RelativePaths.HasExtension(value, extensions))
                Add(path, $"must be a relative path to a {string.Join(", ", extensions)} file, with forward slashes and no \"..\".");
        }

        private void CheckCategories(IReadOnlyList<string>? categories)
        {
            if (categories is null)
            {
                Add("categories", "is required.");
                return;
            }

            Count("categories", categories.Count, 1, 3);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < categories.Count; i++)
            {
                if (!options.Categories.Contains(categories[i], StringComparer.Ordinal))
                    Add(Index("categories", i), $"\"{categories[i]}\" is not a hub category.");
                else if (!seen.Add(categories[i]))
                    Add(Index("categories", i), $"lists \"{categories[i]}\" again.");
            }
        }

        private void CheckKeywords(IReadOnlyList<string>? keywords)
        {
            if (keywords is null)
                return;
            if (keywords.Count > MaxKeywords)
                Add("keywords", $"can have at most {MaxKeywords} items.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < keywords.Count; i++)
            {
                if (!Keyword().IsMatch(keywords[i]))
                    Add(Index("keywords", i), "must be 1 to 30 lowercase letters, digits and hyphens, starting with a letter or digit.");
                else if (!seen.Add(keywords[i]))
                    Add(Index("keywords", i), $"lists \"{keywords[i]}\" again.");
            }
        }

        private void CheckDeprecation(ManifestDeprecation? deprecated, string? ownId)
        {
            if (deprecated is null)
                return;
            if (Required("deprecated.reason", deprecated.Reason) is { } reason)
                Length("deprecated.reason", reason, 1, 200);
            if (deprecated.Replacement is { } replacement && (!PluginId.IsValid(replacement) || string.Equals(replacement, ownId, StringComparison.Ordinal)))
                Add("deprecated.replacement", "must be the ID of another plugin.");
        }

        private void CheckLocalization(DeveloperManifest manifest)
        {
            if (manifest.LanguagePack is { } pack)
            {
                CheckLanguagePack(pack, manifest);
                return;
            }

            if (manifest.Languages is not { } languages)
                return;
            if (languages.Count > MaxLanguages)
                Add("languages", $"can have at most {MaxLanguages} items.");
            var seen = new HashSet<string>(LanguageTags.Comparer);
            for (var i = 0; i < languages.Count; i++)
            {
                var path = Index("languages", i);
                if (LanguageTag(path, languages[i]) && !seen.Add(languages[i]))
                    Add(path, $"lists {languages[i]} again.");
            }

            if (!languages.Contains(LanguageTags.English, LanguageTags.Comparer))
                Add("languages", "must include en, the English source the other languages translate.");
        }

        private void CheckLanguagePack(ManifestLanguagePack pack, DeveloperManifest manifest)
        {
            if (Required("languagePack.language", pack.Language) is { } language)
                LanguageTag("languagePack.language", language);
            if (Required("languagePack.name", pack.Name) is { } name)
                Line("languagePack.name", name, 1, 40);
            if (Required("languagePack.englishName", pack.EnglishName) is { } englishName)
                Line("languagePack.englishName", englishName, 1, 40);

            if (manifest.Languages is not null)
                Add("languages", "is not allowed in a language pack; languagePack.language is the one language it carries.");
            if (manifest.Capabilities is { Count: > 0 })
                Add("capabilities", "must be empty in a language pack, which only carries translations.");
            if (manifest.Dependencies is { Count: > 0 })
                Add("dependencies", "must be empty in a language pack, which only carries translations.");
        }

        private bool LanguageTag(string path, string tag)
        {
            if (LanguageTags.Canonicalize(tag) is not { } canonical)
                Add(path, "must be a BCP 47 language tag such as en, nl, nl-BE or pt-BR.");
            else if (!string.Equals(tag, canonical, StringComparison.Ordinal))
                Add(path, $"must be written {canonical}.");
            else
                return true;
            return false;
        }

        private string? Required(string path, string? value)
        {
            if (value is null)
                Add(path, "is required.");
            return value;
        }

        private void Length(string path, string value, int min, int max)
        {
            if (value.Length < min || value.Length > max)
                Add(path, min == max ? $"must be {min} characters." : $"must be {min} to {max} characters.");
        }

        private void Line(string path, string value, int min, int max)
        {
            Length(path, value, min, max);
            if (value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal))
                Add(path, "must be a single line.");
        }

        private void Count(string path, int count, int min, int max)
        {
            if (count < min || count > max)
                Add(path, $"must have {min} to {max} items.");
        }

        private static string Index(string path, int index) => string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]");

        private void Add(string path, string message) => Problems.Add(new ManifestProblem(path, message));
    }
}
