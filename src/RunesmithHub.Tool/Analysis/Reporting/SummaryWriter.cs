using System.Globalization;
using System.Text;
using RunesmithHub.Tool.Analysis.Api;

namespace RunesmithHub.Tool.Analysis.Reporting;

/// <summary>
/// Writes <c>summary.md</c>, the readable report for the tracking issue and the version pull request: the verdict first, then capability,
/// dependency, maintainer, build logic and web content changes, the languages, the heuristic notice, and the comparison with the previous
/// version.
/// </summary>
internal static class SummaryWriter
{
    public const string FileName = "summary.md";

    /// <summary>Marks where the version pull request adds the comparison's numbers.</summary>
    public const string ComparisonPlaceholder = "<!-- comparison-stats -->";

    private const int SitesShown = 10;

    public static string Write(AnalysisReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        var title = report.Package is { } package ? $"{report.Id} {package.Version}" : report.Id;
        text.Append("# Analysis of ").Append(title).Append("\n\n");
        text.Append("**Verdict: ").Append(Verdict(report.Verdict)).Append("**\n");

        Section(text, "Failures", report.Problems.Where(problem => problem.Severity == ProblemSeverity.Error).Select(problem => problem.Message));
        Section(text, "Needs review because", report.Triggers.Select(TriggerDescriptions.Of));
        Section(text, "Warnings", report.Problems.Where(problem => problem.Severity == ProblemSeverity.Warning).Select(problem => problem.Message));
        if (report.Manifest is null)
            return text.ToString();

        WriteCapabilities(text, report);
        WriteApi(text, report.Api);
        WriteDependencies(text, report);
        WriteMaintainersAndBuildLogic(text, report);
        WriteWebContent(text, report);
        WriteLanguages(text, report);
        text.Append("\n## Review rules\n\n").Append(report.Heuristics switch
        {
            HeuristicsState.Skipped => "Review rules were not run.",
            _ when report.Triggers.Contains(Protocol.Index.ReviewTriggers.Heuristic) => "A review rule matched. An administrator will look at the details.",
            _ => "No review rule matched.",
        }).Append('\n');
        WriteComparison(text, report);
        return text.ToString();
    }

    private static string Verdict(AnalysisOutcome outcome) => outcome switch
    {
        AnalysisOutcome.Passed => "passed",
        AnalysisOutcome.Review => "passed, needs review",
        _ => "failed",
    };

    private static void Section(StringBuilder text, string heading, IEnumerable<string> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return;
        text.Append("\n## ").Append(heading).Append("\n\n");
        foreach (var item in list)
            text.Append("- ").Append(item).Append('\n');
    }

    private static void WriteCapabilities(StringBuilder text, AnalysisReport report)
    {
        var capabilities = report.Capabilities!;
        text.Append("\n## Capabilities\n\n");
        var declared = capabilities.Declared.Select(capability => capability.Id).ToHashSet(StringComparer.Ordinal);
        var detected = capabilities.Detected.ToDictionary(capability => capability.Id, StringComparer.Ordinal);
        var shown = Protocol.Capabilities.All.Where(id => declared.Contains(id) || detected.ContainsKey(id)).ToList();
        if (shown.Count == 0)
            text.Append("None declared or detected.\n");
        else
        {
            text.Append("| Capability | Declared | Detected | Change |\n| --- | --- | --- | --- |\n");
            foreach (var id in shown)
            {
                var change = capabilities.Added.Contains(id) ? "added" : "";
                text.Append(CultureInfo.InvariantCulture, $"| {id} | {(declared.Contains(id) ? "yes" : "no")} | {(detected.ContainsKey(id) ? "yes" : "no")} | {change} |\n");
            }
        }

        if (capabilities.Removed.Count > 0)
            text.Append("\nRemoved since the previous version: ").Append(string.Join(", ", capabilities.Removed)).Append(".\n");
        if (report.Manifest!.NetworkHosts.Count > 0)
            text.Append("\nNetwork hosts: ").Append(string.Join(", ", report.Manifest.NetworkHosts.Select(host => $"`{host}`"))).Append(".\n");
        if (capabilities.NetworkHostsAdded.Count > 0)
            text.Append("\nNetwork hosts added since the previous version: ").Append(string.Join(", ", capabilities.NetworkHostsAdded.Select(host => $"`{host}`"))).Append(".\n");
        foreach (var id in capabilities.Added.Where(detected.ContainsKey))
        {
            var capability = detected[id];
            text.Append("\nWhere ").Append(id).Append(" is used:\n\n");
            foreach (var site in capability.CallSites.Take(SitesShown))
                text.Append("- `").Append(site.Member).Append("` in ").Append(site.Assembly).Append(" uses `").Append(site.Api).Append("`\n");
            if (capability.CallSiteCount > SitesShown)
                text.Append("- and ").Append(capability.CallSiteCount - SitesShown).Append(" more\n");
        }
    }

    private static void WriteApi(StringBuilder text, ApiAnalysis? api)
    {
        if (api is null)
            return;
        text.Append("\n## Contracts since ").Append(api.PreviousVersion).Append("\n\n");
        if (api.Breaking.Count == 0 && api.Additions.Count == 0)
        {
            text.Append("No public API changes.\n");
            return;
        }

        foreach (var change in api.Breaking.Concat(api.Additions))
        {
            var label = change.Kind switch { ApiChangeKind.Removed => "Removed", ApiChangeKind.Changed => "Changed", _ => "Added" };
            text.Append("- ").Append(label).Append(api.Breaking.Contains(change) ? " (breaking)" : "").Append(": `").Append(change.Member).Append("`\n");
        }
    }

    private static void WriteDependencies(StringBuilder text, AnalysisReport report)
    {
        var dependencies = report.Dependencies!;
        text.Append("\n## Dependencies\n\n");
        if (report.Previous is null)
        {
            text.Append("Plugins: ").Append(dependencies.Plugins.Count == 0 ? "none" : string.Join(", ", dependencies.Plugins.Select(plugin => $"{plugin.Id} {plugin.Range}"))).Append(".\n\n");
            var bundled = dependencies.Packages.Where(package => package.Bundled).ToList();
            text.Append("Bundled NuGet packages: ").Append(bundled.Count == 0 ? "none" : string.Join(", ", bundled.Select(package => $"{package.Id} {package.Version}"))).Append(".\n");
            return;
        }

        if (dependencies.Changes.Count == 0)
        {
            text.Append("No changes since ").Append(report.Previous.Version).Append(".\n");
            return;
        }

        foreach (var change in dependencies.Changes)
        {
            var what = change.Kind == "plugin" ? "Plugin" : "NuGet package";
            var detail = change.Change switch
            {
                "added" => $"added at {change.To}",
                "removed" => $"removed (was {change.From})",
                _ => $"moved from {change.From} to {change.To}",
            };
            text.Append("- ").Append(what).Append(' ').Append(change.Id).Append(' ').Append(detail).Append('\n');
        }
    }

    private static void WriteMaintainersAndBuildLogic(StringBuilder text, AnalysisReport report)
    {
        text.Append("\n## Maintainers and build logic\n\n");
        text.Append("Maintainer changes are checked when the version record is written.\n\n");
        if (report.BuildLogic is null)
            text.Append("Build logic was not checked.\n");
        else if (report.BuildLogic.Count == 0)
            text.Append("No custom build logic.\n");
        else
        {
            foreach (var finding in report.BuildLogic)
                text.Append("- ").Append(finding.Kind).Append(' ').Append(finding.Name).Append(" in ").Append(finding.File).Append(" line ").Append(finding.Line).Append('\n');
        }
    }

    private static void WriteWebContent(StringBuilder text, AnalysisReport report)
    {
        if (report.Web is not { } web)
            return;
        text.Append("\n## Web content\n\n");
        text.Append(CultureInfo.InvariantCulture, $"{web.Files} {(web.Files == 1 ? "file" : "files")} under web/.");
        text.Append(web.Code.Count == 0 ? " No JavaScript or WebAssembly.\n" : " JavaScript and WebAssembly:\n\n");
        foreach (var file in web.Code.Take(SitesShown))
            text.Append("- `").Append(file).Append("`\n");
        if (web.Code.Count > SitesShown)
            text.Append("- and ").Append(web.Code.Count - SitesShown).Append(" more\n");
        if (web.Changed is { } changed)
            text.Append('\n').Append(changed.Count == 0 ? "No web files changed since the previous version.\n" : $"Changed since the previous version: {Listed(changed)}.\n");
    }

    private static string Listed(IReadOnlyList<string> files) =>
        string.Join(", ", files.Take(SitesShown).Select(file => $"`{file}`")) + (files.Count > SitesShown ? $" and {files.Count - SitesShown} more" : "");

    private static void WriteLanguages(StringBuilder text, AnalysisReport report)
    {
        if (report.Manifest!.LanguagePack is { } pack)
        {
            text.Append("\n## Language pack\n\n");
            text.Append(CultureInfo.InvariantCulture, $"Adds `{Printable(pack.Language)}`: {Printable(pack.Name)}, in English {Printable(pack.EnglishName)}.\n\n");
            var targets = report.LanguagePack?.Targets ?? [];
            if (targets.Count == 0)
                text.Append("It translates nothing.\n");
            else
                Table(text, "Translates", targets.Select(target => (target.Target, target.Messages)));
            return;
        }

        text.Append("\n## Languages\n\n");
        if (report.Languages is { Count: > 0 } languages)
            Table(text, "Language", languages.Select(language => (Printable(language.Language), language.Messages)));
        else
            text.Append("Not localized: English only, from the plugin's code.\n");
    }

    private static void Table(StringBuilder text, string heading, IEnumerable<(string Name, int Messages)> rows)
    {
        text.Append("| ").Append(heading).Append(" | Messages |\n| --- | --- |\n");
        foreach (var (name, messages) in rows)
            text.Append(CultureInfo.InvariantCulture, $"| {name} | {messages} |\n");
    }

    private static string Printable(string text) => Localization.TranslationCheck.Printable(text);

    private static void WriteComparison(StringBuilder text, AnalysisReport report)
    {
        text.Append("\n## Changes since the previous version\n\n");
        if (report.Previous is not { } previous)
        {
            text.Append("This is the first version.\n");
            return;
        }

        text.Append(CultureInfo.InvariantCulture, $"[Compare {previous.Version} with {report.Package!.Version}]({report.Manifest!.Repository}/compare/{previous.Commit}...{report.Manifest.Build.Commit})\n\n");
        text.Append(ComparisonPlaceholder).Append('\n');
    }
}
