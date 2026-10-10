using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Tool.Analysis.Reporting;

namespace RunesmithHub.Tool.Analysis;

/// <summary>Lists the files under <c>web/</c> that web views show, the code among them, and what changed since the previous version.</summary>
internal static class WebContentCheck
{
    private const int FilesInMessage = 5;

    /// <returns>The web content, or null when neither version has any; and a review problem when the code needs a person's look.</returns>
    public static (WebContentAnalysis? Analysis, AnalysisProblem? Problem) Check(PackageSnapshot package, PackageSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(package);
        var files = WebFiles(package);
        var before = previous is null ? null : WebFiles(previous);
        if (files.Count == 0 && (before is null || before.Count == 0))
            return (null, null);

        var code = files.Keys.Where(PackageEntryRules.IsWebCode).ToList();
        IReadOnlyList<string>? changed = before is null
            ? null
            : [.. files.Keys.Union(before.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Where(file => !files.TryGetValue(file, out var now) || !before.TryGetValue(file, out var then) || now != then)];
        var analysis = new WebContentAnalysis(files.Count, code, changed);
        if (code.Count == 0 || changed is { Count: 0 })
            return (analysis, null);

        var listed = string.Join(", ", code.Take(FilesInMessage)) + (code.Count > FilesInMessage ? $" and {code.Count - FilesInMessage} more" : "");
        var reason = changed is null ? "in a first version" : $"and {changed.Count} web {(changed.Count == 1 ? "file" : "files")} changed since {previous!.Manifest.Version}";
        return (analysis, AnalysisProblem.Review(ProblemCodes.WebContent, $"The web folder holds code web views run, {reason}: {listed}."));
    }

    private static SortedDictionary<string, string> WebFiles(PackageSnapshot package) =>
        new(package.Entries.Where(PackageEntryRules.IsWebFile).ToDictionary(entry => entry, entry => Hashes.Sha256(package.ReadFile(entry)), StringComparer.Ordinal), StringComparer.Ordinal);
}
