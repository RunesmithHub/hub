using RunesmithHub.Protocol.Manifests;

namespace RunesmithHub.Tool.Analysis;

/// <summary>
/// Checks that the plugin's license is OSI-approved and flags bundled packages whose licenses may not combine with it: strong copyleft in a
/// plugin under another license, and Apache-2.0 code in a GPL-2.0-only plugin.
/// </summary>
internal static class LicenseCheck
{
    private static readonly string[] StrongCopyleftPrefixes = ["GPL-", "AGPL-"];

    public static IEnumerable<AnalysisProblem> Check(string pluginLicense, IReadOnlyList<SbomComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (SpdxExpression.Check(pluginLicense, OsiLicenses.IsApproved) is { } problem)
            yield return AnalysisProblem.Error(ProblemCodes.License, $"The plugin's license {pluginLicense} is not usable: {problem}");

        var pluginIds = Ids(pluginLicense);
        foreach (var component in components.Where(component => component.Bundled))
        {
            if (component.License is not { } license)
            {
                yield return AnalysisProblem.Warning(ProblemCodes.LicenseUnknown, $"{component.Id} {component.Version} does not state its license as an SPDX expression; check it by hand.", component.Id);
                continue;
            }

            var ids = Ids(license);
            var copyleft = !license.Contains(" OR ", StringComparison.Ordinal)
                && ids.Any(IsStrongCopyleft) && !ids.Where(IsStrongCopyleft).All(id => pluginIds.Any(own => Family(own) == Family(id)));
            var apacheInGpl2 = pluginIds.SequenceEqual(["GPL-2.0-only"]) && ids.Contains("Apache-2.0");
            if (copyleft || apacheInGpl2)
                yield return AnalysisProblem.Review(ProblemCodes.LicenseCombination, $"{component.Id} {component.Version} is licensed under {license}, which may not combine with the plugin's {pluginLicense}.", component.Id);
        }
    }

    private static string[] Ids(string expression) =>
        [.. expression.Split([' ', '(', ')'], StringSplitOptions.RemoveEmptyEntries).Where(token => token is not ("AND" or "OR" or "WITH"))];

    private static bool IsStrongCopyleft(string id) => StrongCopyleftPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string Family(string id) => id.Split('-')[0].ToUpperInvariant();
}
