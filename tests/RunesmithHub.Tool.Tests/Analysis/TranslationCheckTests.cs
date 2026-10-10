using System.Text;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Localization;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Analysis;

public sealed class TranslationCheckTests : IDisposable
{
    private const string English = "todo-count = { $count } todos\ntodo-empty = Nothing to do\n";
    private const string Dutch = "todo-count = { $count } taken\n";

    private static readonly ManifestLanguagePack Nederlands = new() { Language = "nl", Name = "Nederlands", EnglishName = "Dutch" };

    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static (string Name, byte[] Content) File(string name, string content) => (name, Encoding.UTF8.GetBytes(content));

    private static TestPackage Pack => new()
    {
        Id = "lumen.dutch",
        Name = "Lumen Dutch",
        LanguagePack = Nederlands,
        Resources =
        [
            File("locales/nl/runesmith/shell.ftl", "settings-title = Instellingen\nrun-exited = Het programma stopte met code { $code }.\n"),
            File("locales/nl/runesmith/editor.ftl", "editor-find = Zoeken\n"),
            File("locales/nl/lumen.todo/lumen.todo.ftl", Dutch),
        ],
    };

    private async Task<AnalysisReport> AnalyseAsync(TestPackage package, TestPackage? previous = null)
    {
        var request = new AnalysisRequest(
            package.Write(_folder.Combine($"{Guid.NewGuid():N}.rsplugin")),
            previous?.Write(_folder.Combine($"{Guid.NewGuid():N}.rsplugin")),
            TestRegistrations.For(package.Id, TestPackage.Repository),
            package.WriteSource(_folder.Combine($"{Guid.NewGuid():N}")),
            null);
        return (await new PluginAnalyzer(new FakeVulnerabilities(), package.PackageSource(), TimeProvider.System).AnalyseAsync(request, TestContext.Current.CancellationToken)).Report;
    }

    private static IEnumerable<string> Errors(AnalysisReport report) =>
        report.Problems.Where(problem => problem.Severity == ProblemSeverity.Error).Select(problem => problem.Message);

    [Fact]
    public async Task PassesALocalizedPluginAndCountsTheMessagesOfEachLanguage()
    {
        var report = await AnalyseAsync(new TestPackage
        {
            Languages = ["en", "nl"],
            Resources =
            [
                File("locales/en/lumen.todo.ftl", English),
                File("locales/nl/lumen.todo.ftl", Dutch),
                File("locales/nl/more.ftl", "todo-empty = Niets te doen\ntodo-count = { $count } taken\n-brand = Lumen\n"),
            ],
        });

        Assert.Empty(report.Problems);
        Assert.Equal(["first-version"], report.Triggers);
        Assert.Equal([new LanguageMessages("en", 2), new LanguageMessages("nl", 2)], report.Languages);
        Assert.Null(report.LanguagePack);
    }

    [Fact]
    public async Task SaysNothingOfAPluginThatIsNotLocalized()
    {
        var report = await AnalyseAsync(new TestPackage { Resources = [File("Lumen.Todo.Strings.resources", "not a translation")] });

        Assert.Empty(report.Problems);
        Assert.Null(report.Languages);
        Assert.Null(report.LanguagePack);
    }

    [Fact]
    public async Task FailsListedLanguagesWithoutFilesAndFilesInLanguagesThatAreNotListed()
    {
        var report = await AnalyseAsync(new TestPackage
        {
            Languages = ["en", "nl", "pt-BR"],
            Resources = [File("locales/en/lumen.todo.ftl", English), File("locales/NL/lumen.todo.ftl", Dutch), File("locales/de/lumen.todo.ftl", "todo-count = { $count } Aufgaben\n")],
        });

        Assert.Equal(AnalysisOutcome.Failed, report.Verdict);
        Assert.Equal(
            [
                "plugin.json lists pt-BR in languages, but the implementation embeds no locales/pt-BR/<file>.ftl.",
                "The implementation embeds translations in de, which languages in plugin.json does not list.",
            ],
            Errors(report));
        Assert.All(report.Problems, problem => Assert.Equal(ProblemCodes.Translations, problem.Code));
        Assert.Equal([new LanguageMessages("en", 2), new LanguageMessages("nl", 1), new LanguageMessages("pt-BR", 0), new LanguageMessages("de", 1)], report.Languages);
    }

    [Fact]
    public async Task FailsTranslationsOfAPluginThatListsNoLanguages()
    {
        var report = await AnalyseAsync(new TestPackage { Resources = [File("locales/nl/lumen.todo.ftl", Dutch), File("locales/en/lumen.todo.ftl", English)] });

        Assert.Equal(["The implementation embeds translations (en, nl), but plugin.json lists no languages; list them in languages, with en for the English source."], Errors(report));
    }

    [Fact]
    public async Task FailsFilesThatAreNotFluentWithoutWaitingOnAnUnfinishedLastLine()
    {
        var report = await AnalyseAsync(new TestPackage
        {
            Languages = ["en", "nl"],
            Resources =
            [
                File("locales/en/lumen.todo.ftl", "todo-count = Todos\nbroken\ntodo-empty = Nothing to do\nalso broken\n"),
                File("locales/nl/lumen.todo.ftl", "todo-count = { \"taken"),
                ("locales/nl/latin1.ftl", [0x74, 0x20, 0x3D, 0x20, 0xE9, 0x0A]),
                File("locales/nl/deep.ftl", "deep = " + new string('{', FluentFile.MaxOpenings + 1)),
            ],
        });

        Assert.Equal(
            [
                "locales/en/lumen.todo.ftl has 2 Fluent syntax errors, the first on line 2: Expected a token starting with \"=\" found \"\\u000a\" instead.",
                "locales/nl/lumen.todo.ftl has a Fluent syntax error on line 1: Unterminated string literal.",
                "locales/nl/latin1.ftl is not UTF-8 text.",
                "locales/nl/deep.ftl holds more than 10000 { and ( characters, the most the hub reads in one file; split it into smaller files.",
            ],
            Errors(report));
        Assert.All(report.Problems, problem => Assert.Equal(problem.Message.Split(' ')[0], problem.Location));
        Assert.Equal([new LanguageMessages("en", 2), new LanguageMessages("nl", 0)], report.Languages);
    }

    [Fact]
    public async Task FailsPluginTranslationsThatClaimToBeALanguagePackOrAreMisnamed()
    {
        var report = await AnalyseAsync(new TestPackage
        {
            Languages = ["en"],
            Resources =
            [
                File("locales/en/lumen.todo.ftl", English),
                File("locales/nl/runesmith/shell.ftl", "settings-title = Instellingen\n"),
                File("locales/en.ftl", English),
                File("locales/en_US/lumen.todo.ftl", English),
            ],
        });

        Assert.Equal(
            [
                "locales/nl/runesmith/shell.ftl translates \"runesmith\", which only a language pack does, and plugin.json has no languagePack.",
                "locales/en.ftl is not named locales/<language>/<file>.ftl.",
                "locales/en_US/lumen.todo.ftl is in \"en_US\", which is not a BCP 47 language tag such as en or pt-BR.",
            ],
            Errors(report));
        Assert.Equal([ProblemCodes.LanguagePack, ProblemCodes.Translations, ProblemCodes.Translations], report.Problems.Select(problem => problem.Code));
    }

    [Fact]
    public async Task PassesALanguagePackAndListsWhatItTranslatesRunesmithFirst()
    {
        var report = await AnalyseAsync(Pack);

        Assert.Empty(report.Problems);
        Assert.Equal(["first-version"], report.Triggers);
        Assert.Equal("nl", report.LanguagePack!.Language);
        Assert.Equal([new TargetMessages("runesmith", 3), new TargetMessages("lumen.todo", 1)], report.LanguagePack.Targets);
        Assert.Null(report.Languages);
    }

    [Fact]
    public async Task ReviewsALanguagePackForItsFirstVersionButNotForNewTranslations()
    {
        var next = Pack with { Version = "1.0.1", Resources = [.. Pack.Resources, File("locales/nl/runesmith.git/git.ftl", "git-commit = Vastleggen\n")] };

        var report = await AnalyseAsync(next, Pack);

        Assert.Equal(AnalysisOutcome.Passed, report.Verdict);
        Assert.Empty(report.Triggers);
        Assert.Equal(["runesmith", "lumen.todo", "runesmith.git"], report.LanguagePack!.Targets.Select(target => target.Target));
    }

    [Fact]
    public async Task FailsALanguagePackThatCarriesAnythingButItsOwnLanguageForRunesmithOrPlugins()
    {
        var report = await AnalyseAsync(Pack with
        {
            Resources =
            [
                .. Pack.Resources,
                File("locales/de/runesmith/shell.ftl", "settings-title = Einstellungen\n"),
                File("locales/nl/Lumen.Todo/lumen.todo.ftl", Dutch),
                File("locales/nl/shell.ftl", "settings-title = Instellingen\n"),
                File("Lumen.Dutch.Strings.resources", "other"),
            ],
        });

        Assert.Equal(AnalysisOutcome.Failed, report.Verdict);
        Assert.Equal(
            [
                "locales/de/runesmith/shell.ftl is in \"de\", but the language pack adds nl and carries no other language.",
                "locales/nl/Lumen.Todo/lumen.todo.ftl translates \"Lumen.Todo\", which is neither runesmith nor a plugin ID.",
                "locales/nl/shell.ftl is not named locales/nl/<target>/<file>.ftl, and a language pack carries nothing but its translations.",
                "Lumen.Dutch.Strings.resources is not named locales/nl/<target>/<file>.ftl, and a language pack carries nothing but its translations.",
            ],
            Errors(report));
        Assert.All(report.Problems, problem => Assert.Equal(ProblemCodes.LanguagePack, problem.Code));
    }

    [Fact]
    public async Task FailsALanguagePackWithoutTranslationsOrWithTooManyTargets()
    {
        var empty = await AnalyseAsync(Pack with { Resources = [] });
        var many = await AnalyseAsync(Pack with
        {
            Resources = [.. Enumerable.Range(0, TranslationCheck.MaxTargets + 1).Select(i => File($"locales/nl/lumen.plugin{i}/strings.ftl", "greeting = Hallo\n"))],
        });

        Assert.Equal(["The language pack embeds no translations; add files named locales/nl/<target>/<file>.ftl, where the target is runesmith or a plugin ID."], Errors(empty));
        Assert.Equal(["The language pack translates 101 targets; a language pack translates at most 100."], Errors(many));
    }

    [Fact]
    public void FailsAPackagedManifestOfALanguagePackThatListsLanguagesOrDeclaresCapabilities()
    {
        using var package = PackageSnapshot.Open(Pack.Write(_folder.Combine("pack.rsplugin")), PackageLimits.Default);
        var manifest = package.Manifest with { Languages = ["en", "nl"], Capabilities = [new DeclaredCapability { Id = "process", Reason = "Runs tools." }] };

        var problems = PackagedManifestCheck.Check(manifest, TestRegistrations.For("lumen.dutch", TestPackage.Repository));

        Assert.Equal(
            [
                "plugin.json: languages: is not allowed in a language pack; languagePack.language is the one language it carries.",
                "plugin.json: capabilities: must be empty in a language pack, which only carries translations.",
            ],
            problems.Select(problem => problem.Message));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("two\nlines", "two\\u000alines")]
    [InlineData("tab\there", "tab\\u0009here")]
    [InlineData("line\u2028separator", "line\\u2028separator")]
    public void EscapesControlCharactersInTextFromAPackage(string text, string printable) => Assert.Equal(printable, TranslationCheck.Printable(text));
}
