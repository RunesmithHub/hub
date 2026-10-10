using System.Globalization;
using System.Text;
using Mono.Cecil;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Tool.Analysis.Reporting;

namespace RunesmithHub.Tool.Analysis.Localization;

/// <summary>The languages of a plugin's own translations, or the targets of a language pack, with their messages; and what is wrong with them.</summary>
internal sealed record TranslationResult(IReadOnlyList<LanguageMessages>? Languages, LanguagePackAnalysis? LanguagePack, IReadOnlyList<AnalysisProblem> Problems);

/// <summary>
/// Checks the translations the implementation assembly embeds against the manifest. A plugin's are resources named
/// <c>locales/&lt;language&gt;/&lt;file&gt;.ftl</c>, at least one for each language it lists and none for others. A language pack's resources
/// are all <c>locales/&lt;language&gt;/&lt;target&gt;/&lt;file&gt;.ftl</c> in its one language, for <c>runesmith</c> or a plugin ID. Every file
/// must parse as Fluent.
/// </summary>
internal static class TranslationCheck
{
    /// <summary>The most targets a language pack may translate.</summary>
    public const int MaxTargets = 100;

    private const string Folder = "locales";
    private const string Extension = ".ftl";

    public static TranslationResult Check(PackagedManifest manifest, LoadedAssembly? implementation)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (implementation is null)
            return new TranslationResult(null, null, []);
        var resources = implementation.Definition.MainModule.Resources;
        return manifest.LanguagePack is { } pack ? CheckPack(pack, resources) : CheckPlugin(manifest.Languages, resources);
    }

    /// <summary>Escapes control characters, so text from a package can't break the lines of a report.</summary>
    public static string Printable(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var printable = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsControl(c) || c is '\u2028' or '\u2029')
                printable.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else
                printable.Append(c);
        }

        return printable.ToString();
    }

    private static TranslationResult CheckPlugin(IReadOnlyList<string>? declared, IEnumerable<Resource> resources)
    {
        var problems = new List<AnalysisProblem>();
        var files = new List<(string Language, Resource Resource)>();
        foreach (var resource in resources.Where(resource => resource.Name.StartsWith(Folder + "/", StringComparison.Ordinal)))
        {
            var name = Printable(resource.Name);
            var parts = resource.Name.Split('/');
            if (parts.Length == 4 && IsFileName(parts[3]))
                problems.Add(AnalysisProblem.Error(ProblemCodes.LanguagePack, $"{name} translates \"{Printable(parts[2])}\", which only a language pack does, and plugin.json has no languagePack.", name));
            else if (parts.Length != 3 || !IsFileName(parts[2]))
                problems.Add(AnalysisProblem.Error(ProblemCodes.Translations, $"{name} is not named locales/<language>/<file>.ftl.", name));
            else if (LanguageTags.Canonicalize(parts[1]) is not { } language)
                problems.Add(AnalysisProblem.Error(ProblemCodes.Translations, $"{name} is in \"{Printable(parts[1])}\", which is not a BCP 47 language tag such as en or pt-BR.", name));
            else
                files.Add((language, resource));
        }

        var found = MessagesBy(files, LanguageTags.Comparer, problems);
        if (declared is null)
        {
            if (found.Count > 0)
                problems.Add(AnalysisProblem.Error(ProblemCodes.Translations,
                    $"The implementation embeds translations ({string.Join(", ", found.Keys.Order(StringComparer.Ordinal))}), but plugin.json lists no languages; list them in languages, with en for the English source."));
            return new TranslationResult(found.Count == 0 ? null : Counts(found.Keys.Order(StringComparer.Ordinal), found), null, problems);
        }

        foreach (var language in declared.Where(language => !found.ContainsKey(language)).Select(Printable))
            problems.Add(AnalysisProblem.Error(ProblemCodes.Translations, $"plugin.json lists {language} in languages, but the implementation embeds no locales/{language}/<file>.ftl."));
        var undeclared = found.Keys.Where(language => !declared.Contains(language, LanguageTags.Comparer)).Order(StringComparer.Ordinal).ToList();
        foreach (var language in undeclared)
            problems.Add(AnalysisProblem.Error(ProblemCodes.Translations, $"The implementation embeds translations in {language}, which languages in plugin.json does not list."));
        return new TranslationResult(Counts(declared.Concat(undeclared), found), null, problems);
    }

    private static TranslationResult CheckPack(DeclaredLanguagePack pack, IEnumerable<Resource> resources)
    {
        var problems = new List<AnalysisProblem>();
        var files = new List<(string Target, Resource Resource)>();
        var language = Printable(pack.Language);
        foreach (var resource in resources)
        {
            var name = Printable(resource.Name);
            var parts = resource.Name.Split('/');
            if (parts.Length != 4 || parts[0] != Folder || !IsFileName(parts[3]))
                problems.Add(AnalysisProblem.Error(ProblemCodes.LanguagePack, $"{name} is not named locales/{language}/<target>/<file>.ftl, and a language pack carries nothing but its translations.", name));
            else if (!LanguageTags.Comparer.Equals(parts[1], pack.Language))
                problems.Add(AnalysisProblem.Error(ProblemCodes.LanguagePack, $"{name} is in \"{Printable(parts[1])}\", but the language pack adds {language} and carries no other language.", name));
            else if (parts[2] != LanguagePackSummary.RunesmithTarget && !PluginId.IsValid(parts[2]))
                problems.Add(AnalysisProblem.Error(ProblemCodes.LanguagePack, $"{name} translates \"{Printable(parts[2])}\", which is neither runesmith nor a plugin ID.", name));
            else
                files.Add((parts[2], resource));
        }

        var found = MessagesBy(files, StringComparer.Ordinal, problems);
        if (found.Count == 0)
            problems.Add(AnalysisProblem.Error(ProblemCodes.LanguagePack, $"The language pack embeds no translations; add files named locales/{language}/<target>/<file>.ftl, where the target is runesmith or a plugin ID."));
        if (found.Count > MaxTargets)
            problems.Add(AnalysisProblem.Error(ProblemCodes.LanguagePack, $"The language pack translates {found.Count} targets; a language pack translates at most {MaxTargets}."));
        var targets = found.Keys
            .OrderBy(target => target == LanguagePackSummary.RunesmithTarget ? 0 : 1)
            .ThenBy(target => target, StringComparer.Ordinal)
            .Select(target => new TargetMessages(target, found[target].Count));
        return new TranslationResult(null, new LanguagePackAnalysis(pack.Language, [.. targets]), problems);
    }

    private static bool IsFileName(string name) => name.Length > Extension.Length && name.EndsWith(Extension, StringComparison.Ordinal);

    /// <summary>Parses the embedded files and collects the IDs of their messages for each key, a language or a target.</summary>
    private static Dictionary<string, HashSet<string>> MessagesBy(List<(string Key, Resource Resource)> files, StringComparer keys, List<AnalysisProblem> problems)
    {
        var embedded = new List<(string Key, string Name, byte[] Content)>();
        foreach (var (key, resource) in files)
        {
            var name = Printable(resource.Name);
            if (resource is EmbeddedResource data)
                embedded.Add((key, name, data.GetResourceData()));
            else
                problems.Add(AnalysisProblem.Error(ProblemCodes.Translations, $"{name} is not embedded in the assembly.", name));
        }

        var parsed = FluentFile.Parse([.. embedded.Select(file => file.Content)]);
        var messages = new Dictionary<string, HashSet<string>>(keys);
        for (var i = 0; i < embedded.Count; i++)
        {
            var (key, name, _) = embedded[i];
            if (parsed[i].Problem is { } problem)
                problems.Add(AnalysisProblem.Error(ProblemCodes.Translations, $"{name} {problem}", name));
            if (!messages.TryGetValue(key, out var ids))
                messages[key] = ids = new HashSet<string>(StringComparer.Ordinal);
            ids.UnionWith(parsed[i].Messages);
        }

        return messages;
    }

    private static List<LanguageMessages> Counts(IEnumerable<string> languages, Dictionary<string, HashSet<string>> found) =>
        [.. languages.Select(language => new LanguageMessages(language, found.TryGetValue(language, out var ids) ? ids.Count : 0))];
}
