using System.CommandLine;
using System.Text;
using RunesmithHub.Protocol.Signing;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Heuristics;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Analysis.Vulnerabilities;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Findings;
using RunesmithHub.Tool.Keys;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub analyse</c>: the trusted analysis of a built package.</summary>
internal static class AnalyseCommands
{
    public static Command Create(ToolContext context)
    {
        var package = new Option<string>("--package") { Description = "The built .rsplugin package.", Required = true };
        var previous = new Option<string>("--previous") { Description = "The previous published version's package; omit for a first version." };
        var registration = new Option<string>("--registration") { Description = "The plugin's registration.json.", Required = true };
        var source = new Option<string>("--source") { Description = "The checkout of the plugin's repository at the release commit, read as data.", Required = true };
        var commit = new Option<string>("--commit") { Description = "The full SHA of the release commit.", Required = true };
        var output = new Option<string>("--output") { Description = "The folder to write analysis.json, summary.md and the SBOM to.", Required = true };
        var rules = new Option<string>("--rules") { Description = $"The heuristic rules file; {HeuristicRuleSet.FileVariable} by default. Without one, heuristics are skipped." };
        var feed = BuildCommands.FeedOption();
        var command = new Command("analyse", "Analyse a built package against the previous version; exit code 1 when it fails.") { package, previous, registration, source, commit, output, rules, feed };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var rulesFile = result.GetValue(rules) ?? context.GetEnvironmentVariable(HeuristicRuleSet.FileVariable);
            var recipient = FindingsRecipient(context);
            var request = new AnalysisRequest(
                result.GetRequiredValue(package),
                result.GetValue(previous),
                RegistrationFile.Read(result.GetRequiredValue(registration)),
                new AnalysisSource(result.GetRequiredValue(source), result.GetRequiredValue(commit)),
                rulesFile is { Length: > 0 } ? HeuristicRuleSet.Read(rulesFile) : null);
            var analyzer = new PluginAnalyzer(new OsvDatabase(context.Services.Http()), BuildCommands.PackageSource(context, result.GetValue(feed), null), context.Time);
            var analysis = await analyzer.AnalyseAsync(request, cancellationToken).ConfigureAwait(false);
            await AnalysisOutput.WriteAsync(analysis, result.GetRequiredValue(output), recipient, cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync(SummaryWriter.Write(analysis.Report)).ConfigureAwait(false);
            return analysis.Report.Verdict == AnalysisOutcome.Failed ? 1 : 0;
        }));
        return command;
    }

    private static PublicKey? FindingsRecipient(ToolContext context) =>
        context.GetEnvironmentVariable(FindingsCipher.RecipientVariable) is { Length: > 0 } json
            ? KeyFiles.ParsePublicKey(Encoding.UTF8.GetBytes(json), FindingsCipher.RecipientVariable)
            : null;
}
