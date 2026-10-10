using System.Text;
using RunesmithHub.Protocol.Manifests;

namespace RunesmithHub.Protocol.Tests.Manifests;

public sealed class ManifestValidatorTests
{
    private const string ValidManifest = """
        {
          "schemaVersion": 1,
          "id": "lumen.todo",
          "name": "Lumen Todo",
          "version": "1.0.0",
          "summary": "Collects TODO comments across your project and links them to issues.",
          "authors": ["Lumen Labs"],
          "license": "MIT",
          "repository": "https://github.com/lumen-labs/lumen-todo",
          "homepage": "https://lumen.example",
          "runesmithApi": "^0.1.0",
          "projects": { "contracts": "src/Lumen.Todo.Contracts/Lumen.Todo.Contracts.csproj", "implementation": "src/Lumen.Todo/Lumen.Todo.csproj" },
          "dependencies": [{ "id": "harbor.issues", "range": ">=1.2.0" }, { "id": "runesmith.git", "range": "^0.3.1" }],
          "capabilities": [{ "id": "network", "reason": "Reads issues from your tracker." }],
          "networkHosts": ["api.github.com", "*"],
          "platforms": ["windows", "linux"],
          "icon": "assets/icon.png",
          "banner": "assets/banner.jpg",
          "gallery": [{ "path": "assets/shot.webp", "caption": "The TODO list" }],
          "categories": ["Productivity"],
          "keywords": ["todo", "issues"],
          "deprecated": { "reason": "Moved to a new plugin.", "replacement": "lumen.tasks" },
          "languages": ["en", "nl", "pt-BR"]
        }
        """;

    private const string LanguagePackManifest = """
        {
          "schemaVersion": 1,
          "id": "lumen.dutch",
          "name": "Lumen Dutch",
          "version": "1.0.0",
          "summary": "Runesmith and Lumen Todo in Dutch.",
          "authors": ["Lumen Labs"],
          "license": "MIT",
          "repository": "https://github.com/lumen-labs/lumen-dutch",
          "runesmithApi": "^0.1.0",
          "projects": { "contracts": "src/Lumen.Dutch.Contracts/Lumen.Dutch.Contracts.csproj", "implementation": "src/Lumen.Dutch/Lumen.Dutch.csproj" },
          "icon": "icon.png",
          "categories": ["Other"],
          "languagePack": { "language": "nl", "name": "Nederlands", "englishName": "Dutch" }
        }
        """;

    [Fact]
    public void AcceptsACompleteValidManifest()
    {
        var result = ManifestValidator.Validate(Encoding.UTF8.GetBytes(ValidManifest));

        Assert.Empty(result.Problems);
        Assert.True(result.IsValid);
        Assert.Equal("lumen.todo", result.Manifest.Id);
        Assert.Equal(["en", "nl", "pt-BR"], result.Manifest.Languages);
    }

    [Fact]
    public void AcceptsALanguagePack()
    {
        var json = LanguagePackManifest.Replace("\"categories\": [\"Other\"],", "\"categories\": [\"Other\"], \"capabilities\": [], \"dependencies\": [],", StringComparison.Ordinal);

        var result = ManifestValidator.Validate(Encoding.UTF8.GetBytes(json));

        Assert.Empty(result.Problems);
        Assert.Equal(new ManifestLanguagePack { Language = "nl", Name = "Nederlands", EnglishName = "Dutch" }, result.Manifest!.LanguagePack);
        Assert.Null(result.Manifest.Languages);
    }

    public static TheoryData<string, string, string> InvalidLanguagePacks => new()
    {
        { "\"language\": \"nl\"", "\"language\": \"NL\"", "languagePack.language" },
        { "\"language\": \"nl\"", "\"language\": \"Dutch\"", "languagePack.language" },
        { "\"language\": \"nl\", ", "", "languagePack.language" },
        { "\"name\": \"Nederlands\"", "\"name\": \"\"", "languagePack.name" },
        { "\"name\": \"Nederlands\"", "\"name\": \"" + new string('n', 41) + "\"", "languagePack.name" },
        { "\"englishName\": \"Dutch\"", "\"englishName\": \"Dutch\\nFlemish\"", "languagePack.englishName" },
        { ", \"englishName\": \"Dutch\"", "", "languagePack.englishName" },
        { "\"categories\": [\"Other\"],", "\"categories\": [\"Other\"], \"languages\": [\"en\", \"nl\"],", "languages" },
        { "\"categories\": [\"Other\"],", "\"categories\": [\"Other\"], \"capabilities\": [{ \"id\": \"process\", \"reason\": \"Runs tools.\" }],", "capabilities" },
        { "\"categories\": [\"Other\"],", "\"categories\": [\"Other\"], \"dependencies\": [{ \"id\": \"runesmith.git\", \"range\": \"^0.1.0\" }],", "dependencies" },
    };

    [Theory]
    [MemberData(nameof(InvalidLanguagePacks))]
    public void ReportsAnInvalidLanguagePackAtItsPath(string original, string replacement, string path)
    {
        Assert.Contains(original, LanguagePackManifest, StringComparison.Ordinal);
        var json = LanguagePackManifest.Replace(original, replacement, StringComparison.Ordinal);

        var result = ManifestValidator.Validate(Encoding.UTF8.GetBytes(json));

        Assert.Contains(result.Problems, problem => problem.Path == path);
    }

    [Fact]
    public void ExplainsWhatIsWrongWithTheLanguages()
    {
        var manifest = ManifestValidator.Validate(Encoding.UTF8.GetBytes(ValidManifest)).Manifest! with { Languages = ["nl", "NL", "nl", "nl_BE"] };

        var problems = ManifestValidator.Validate(manifest).Select(problem => problem.ToString());

        Assert.Equal(
            [
                "languages[1]: must be written nl.",
                "languages[2]: lists nl again.",
                "languages[3]: must be a BCP 47 language tag such as en, nl, nl-BE or pt-BR.",
                "languages: must include en, the English source the other languages translate.",
            ],
            problems);
    }

    [Fact]
    public void AcceptsAMinimalManifest()
    {
        const string minimal = """
            {
              "schemaVersion": 1, "id": "lumen.todo", "name": "Lumen Todo", "version": "0.1.0", "summary": "Todo.", "authors": ["Lumen"],
              "license": "Apache-2.0 OR MIT", "repository": "https://github.com/lumen/todo", "runesmithApi": ">=0.1.0",
              "projects": { "contracts": "A.Contracts.csproj", "implementation": "A.csproj" }, "icon": "icon.webp", "categories": ["Other"]
            }
            """;

        Assert.Empty(ManifestValidator.Validate(Encoding.UTF8.GetBytes(minimal)).Problems);
    }

    [Theory]
    [InlineData("^0.1.1", ">=0.1.1 <0.2.0")]
    [InlineData(">=0.1.1", ">=0.1.1 <0.2.0")]
    public void AcceptsTheRunesmithApiOfAPatchRelease(string range, string normalised)
    {
        var json = ValidManifest.Replace("\"runesmithApi\": \"^0.1.0\"", $"\"runesmithApi\": \"{range}\"", StringComparison.Ordinal);

        var result = ManifestValidator.Validate(Encoding.UTF8.GetBytes(json));

        Assert.Empty(result.Problems);
        Assert.Equal(normalised, Protocol.Versioning.VersionRange.Parse(result.Manifest!.RunesmithApi!).Normalize().ToString());
    }

    [Fact]
    public void AcceptsNoNetworkHostsWithoutTheNetworkCapability()
    {
        var json = ValidManifest
            .Replace("{ \"id\": \"network\", \"reason\": \"Reads issues from your tracker.\" }", "{ \"id\": \"filesystem\", \"reason\": \"Reads files.\" }", StringComparison.Ordinal)
            .Replace("\"networkHosts\": [\"api.github.com\", \"*\"],", "", StringComparison.Ordinal);

        Assert.Empty(ManifestValidator.Validate(Encoding.UTF8.GetBytes(json)).Problems);
    }

    [Fact]
    public void ReportsNetworkHostsWithoutTheNetworkCapability()
    {
        var json = ValidManifest.Replace(
            "{ \"id\": \"network\", \"reason\": \"Reads issues from your tracker.\" }", "{ \"id\": \"filesystem\", \"reason\": \"Reads files.\" }", StringComparison.Ordinal);

        var problem = Assert.Single(ManifestValidator.Validate(Encoding.UTF8.GetBytes(json)).Problems);

        Assert.Equal("networkHosts", problem.Path);
        Assert.Contains("only allowed with the network capability", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAnUnknownPropertyWithItsPath()
    {
        var json = ValidManifest.Replace("\"homepage\"", "\"homepag\"", StringComparison.Ordinal);

        var result = ManifestValidator.Validate(Encoding.UTF8.GetBytes(json));

        Assert.Null(result.Manifest);
        var problem = Assert.Single(result.Problems);
        Assert.Contains("homepag", problem.Path + problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAWrongJsonTypeWithItsPath()
    {
        var json = ValidManifest.Replace("\"schemaVersion\": 1", "\"schemaVersion\": \"1\"", StringComparison.Ordinal);

        var problem = Assert.Single(ManifestValidator.Validate(Encoding.UTF8.GetBytes(json)).Problems);

        Assert.Equal("schemaVersion", problem.Path);
    }

    [Fact]
    public void ReportsEveryMissingRequiredField()
    {
        var problems = ManifestValidator.Validate(new DeveloperManifest());

        Assert.Equal(
            ["schemaVersion", "id", "version", "repository", "name", "summary", "authors", "license", "runesmithApi", "projects", "icon", "categories"],
            problems.Select(problem => problem.Path));
    }

    public static TheoryData<string, string, string> InvalidFields => new()
    {
        { "\"schemaVersion\": 1", "\"schemaVersion\": 2", "schemaVersion" },
        { "\"id\": \"lumen.todo\"", "\"id\": \"Lumen.Todo\"", "id" },
        { "\"name\": \"Lumen Todo\"", "\"name\": \"L\"", "name" },
        { "\"name\": \"Lumen Todo\"", "\"name\": \"" + new string('n', 41) + "\"", "name" },
        { "\"version\": \"1.0.0\"", "\"version\": \"1.0\"", "version" },
        { "\"version\": \"1.0.0\"", "\"version\": \"1.0.0+build\"", "version" },
        { "\"summary\": \"Collects TODO comments across your project and links them to issues.\"", "\"summary\": \"Two\\nlines\"", "summary" },
        { "\"summary\": \"Collects TODO comments across your project and links them to issues.\"", "\"summary\": \"" + new string('s', 121) + "\"", "summary" },
        { "\"authors\": [\"Lumen Labs\"]", "\"authors\": []", "authors" },
        { "\"authors\": [\"Lumen Labs\"]", "\"authors\": [\"\"]", "authors[0]" },
        { "\"license\": \"MIT\"", "\"license\": \"Proprietary\"", "license" },
        { "\"license\": \"MIT\"", "\"license\": \"MIT AND\"", "license" },
        { "\"license\": \"MIT\"", "\"license\": \"(MIT\"", "license" },
        { "\"repository\": \"https://github.com/lumen-labs/lumen-todo\"", "\"repository\": \"https://gitlab.com/lumen/todo\"", "repository" },
        { "\"homepage\": \"https://lumen.example\"", "\"homepage\": \"http://lumen.example\"", "homepage" },
        { "\"runesmithApi\": \"^0.1.0\"", "\"runesmithApi\": \"*\"", "runesmithApi" },
        { "\"runesmithApi\": \"^0.1.0\"", "\"runesmithApi\": \">=0.1.0 <0.3.0\"", "runesmithApi" },
        { "\"runesmithApi\": \"^0.1.0\"", "\"runesmithApi\": \"^0.1.0 || ^0.2.0\"", "runesmithApi" },
        { "\"contracts\": \"src/Lumen.Todo.Contracts/Lumen.Todo.Contracts.csproj\"", "\"contracts\": \"../Lumen.Todo.Contracts.csproj\"", "projects.contracts" },
        { "\"contracts\": \"src/Lumen.Todo.Contracts/Lumen.Todo.Contracts.csproj\"", "\"contracts\": \"src\\\\A.csproj\"", "projects.contracts" },
        { "\"implementation\": \"src/Lumen.Todo/Lumen.Todo.csproj\"", "\"implementation\": \"/src/Lumen.Todo.csproj\"", "projects.implementation" },
        { "\"implementation\": \"src/Lumen.Todo/Lumen.Todo.csproj\"", "\"implementation\": \"src/Lumen.Todo/Lumen.Todo.txt\"", "projects.implementation" },
        { "{ \"id\": \"harbor.issues\", \"range\": \">=1.2.0\" }", "{ \"id\": \"lumen.todo\", \"range\": \">=1.2.0\" }", "dependencies[0].id" },
        { "{ \"id\": \"harbor.issues\", \"range\": \">=1.2.0\" }", "{ \"id\": \"runesmith.git\", \"range\": \">=1.2.0\" }", "dependencies[1].id" },
        { "{ \"id\": \"harbor.issues\", \"range\": \">=1.2.0\" }", "{ \"id\": \"harbor.issues\", \"range\": \"latest\" }", "dependencies[0].range" },
        { "{ \"id\": \"network\", \"reason\": \"Reads issues from your tracker.\" }", "{ \"id\": \"telepathy\", \"reason\": \"Why.\" }", "capabilities[0].id" },
        { "{ \"id\": \"network\", \"reason\": \"Reads issues from your tracker.\" }", "{ \"id\": \"network\", \"reason\": \"\" }", "capabilities[0].reason" },
        { "\"networkHosts\": [\"api.github.com\", \"*\"]", "\"networkHosts\": []", "networkHosts" },
        { "\"networkHosts\": [\"api.github.com\", \"*\"]", "\"networkHosts\": [\"not a host\"]", "networkHosts[0]" },
        { "\"networkHosts\": [\"api.github.com\", \"*\"]", "\"networkHosts\": [\"api.github.com\", \"https://api.github.com\"]", "networkHosts[1]" },
        { "\"networkHosts\": [\"api.github.com\", \"*\"]", "\"networkHosts\": [\"\"]", "networkHosts[0]" },
        { "{ \"id\": \"network\", \"reason\": \"Reads issues from your tracker.\" }", "{ \"id\": \"filesystem\", \"reason\": \"Reads files.\" }", "networkHosts" },
        { "\"platforms\": [\"windows\", \"linux\"]", "\"platforms\": [\"windows\", \"bsd\"]", "platforms[1]" },
        { "\"platforms\": [\"windows\", \"linux\"]", "\"platforms\": [\"linux\", \"linux\"]", "platforms[1]" },
        { "\"icon\": \"assets/icon.png\"", "\"icon\": \"assets/icon.svg\"", "icon" },
        { "\"icon\": \"assets/icon.png\"", "\"icon\": \"C:/icon.png\"", "icon" },
        { "\"banner\": \"assets/banner.jpg\"", "\"banner\": \"assets/banner.gif\"", "banner" },
        { "\"caption\": \"The TODO list\"", "\"caption\": \"\"", "gallery[0].caption" },
        { "\"categories\": [\"Productivity\"]", "\"categories\": []", "categories" },
        { "\"categories\": [\"Productivity\"]", "\"categories\": [\"Games\"]", "categories[0]" },
        { "\"categories\": [\"Productivity\"]", "\"categories\": [\"Themes\", \"Other\", \"Testing\", \"Education\"]", "categories" },
        { "\"keywords\": [\"todo\", \"issues\"]", "\"keywords\": [\"Todo\"]", "keywords[0]" },
        { "\"keywords\": [\"todo\", \"issues\"]", "\"keywords\": [\"todo\", \"todo\"]", "keywords[1]" },
        { "\"replacement\": \"lumen.tasks\"", "\"replacement\": \"lumen.todo\"", "deprecated.replacement" },
        { "\"reason\": \"Moved to a new plugin.\"", "\"reason\": \"\"", "deprecated.reason" },
        { "\"languages\": [\"en\", \"nl\", \"pt-BR\"]", "\"languages\": [\"nl\", \"pt-BR\"]", "languages" },
        { "\"languages\": [\"en\", \"nl\", \"pt-BR\"]", "\"languages\": []", "languages" },
        { "\"languages\": [\"en\", \"nl\", \"pt-BR\"]", "\"languages\": [\"en\", \"nl\", \"pt-br\"]", "languages[2]" },
        { "\"languages\": [\"en\", \"nl\", \"pt-BR\"]", "\"languages\": [\"en\", \"nl\", \"nl\"]", "languages[2]" },
        { "\"languages\": [\"en\", \"nl\", \"pt-BR\"]", "\"languages\": [\"en\", \"Dutch\"]", "languages[1]" },
        { "\"languages\": [\"en\", \"nl\", \"pt-BR\"]", "\"languages\": [\"en\", \"nl\"], \"languagePack\": { \"language\": \"de\", \"name\": \"Deutsch\", \"englishName\": \"German\" }", "languages" },
    };

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public void ReportsAnInvalidFieldAtItsPath(string original, string replacement, string path)
    {
        Assert.Contains(original, ValidManifest, StringComparison.Ordinal);
        var json = ValidManifest.Replace(original, replacement, StringComparison.Ordinal);

        var result = ManifestValidator.Validate(Encoding.UTF8.GetBytes(json));

        Assert.Contains(result.Problems, problem => problem.Path == path);
    }

    [Fact]
    public void LimitsTheNumberOfDependenciesGalleryImagesKeywordsAndLanguages()
    {
        var manifest = ManifestValidator.Validate(Encoding.UTF8.GetBytes(ValidManifest)).Manifest! with
        {
            Dependencies = [.. Enumerable.Range(0, 33).Select(i => new ManifestDependency { Id = $"dep.plugin{i}", Range = "^1.0.0" })],
            Gallery = [.. Enumerable.Range(0, 7).Select(i => new ManifestGalleryImage { Path = $"shot{i}.png", Caption = "Shot" })],
            Keywords = [.. Enumerable.Range(0, 11).Select(i => $"word{i}")],
            Languages = ["en", .. Enumerable.Range(0, 100).Select(i => $"en-{i:D3}")],
        };

        var paths = ManifestValidator.Validate(manifest).Select(problem => problem.Path).ToList();

        Assert.Contains("dependencies", paths);
        Assert.Contains("gallery", paths);
        Assert.Contains("keywords", paths);
        Assert.Contains("languages", paths);
    }

    [Fact]
    public void ChecksTheRegistrationAndTagWhenGiven()
    {
        var options = new ManifestValidationOptions { ExpectedId = "lumen.tasks", ExpectedRepository = "https://github.com/lumen/tasks", ExpectedVersion = "1.0.1" };

        var paths = ManifestValidator.Validate(Encoding.UTF8.GetBytes(ValidManifest), options).Problems.Select(problem => problem.Path);

        Assert.Equal(["id", "version", "repository"], paths);
    }

    [Theory]
    [InlineData("MIT")]
    [InlineData("mit")]
    [InlineData("Apache-2.0 OR MIT")]
    [InlineData("(MIT OR Apache-2.0) AND BSD-3-Clause")]
    [InlineData("GPL-2.0-or-later WITH Classpath-exception-2.0")]
    [InlineData("LGPL-2.1-only+")]
    public void AcceptsOsiApprovedSpdxExpressions(string expression)
    {
        Assert.Null(SpdxExpression.Check(expression, OsiLicenses.IsApproved));
    }

    [Theory]
    [InlineData("")]
    [InlineData("MIT OR")]
    [InlineData("MIT and Apache-2.0")]
    [InlineData("(MIT")]
    [InlineData("MIT)")]
    [InlineData("CC-BY-NC-4.0")]
    [InlineData("LicenseRef-Mine")]
    [InlineData("MIT WITH")]
    public void RejectsInvalidOrUnapprovedSpdxExpressions(string expression)
    {
        Assert.NotNull(SpdxExpression.Check(expression, OsiLicenses.IsApproved));
    }
}
