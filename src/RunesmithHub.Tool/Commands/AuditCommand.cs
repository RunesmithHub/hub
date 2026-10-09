using System.CommandLine;
using System.Globalization;
using RunesmithHub.Tool.Audit;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub audit</c>: the nightly audit.</summary>
internal static class AuditCommand
{
    public static Command Create(ToolContext context)
    {
        var registry = CommonOptions.Registry();
        var current = CommonOptions.Current();
        var command = new Command("audit", "Check repositories, verified requirements, advisories against published SBOMs and metadata expiry; open an issue for each finding.")
        {
            registry, current,
        };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var reader = new RegistryReader(new DirectoryRegistryFiles(result.GetRequiredValue(registry)));
            var trust = TrustRoot.Verify(await reader.RootsAsync(cancellationToken).ConfigureAwait(false));
            var deployed = await DeployedIndex.LoadAsync(context.Services.Index(result.GetRequiredValue(current)), trust, context.Time, cancellationToken).ConfigureAwait(false);
            var audit = new NightlyAudit(context.Services.GitHub(), context.Services.Bot(), context.Services.Downloads(), context.Services.Advisories(), context.Time);
            var findings = await audit.RunAsync(await reader.LoadAsync(cancellationToken).ConfigureAwait(false), trust, deployed, cancellationToken).ConfigureAwait(false);
            foreach (var finding in findings)
                await writer.WriteLineAsync(finding.Title).ConfigureAwait(false);
            await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{findings.Count} finding(s).")).ConfigureAwait(false);
            return 0;
        }));
        return command;
    }
}
