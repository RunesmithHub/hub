using System.CommandLine;
using System.Globalization;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Registry;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub version</c>: finds what a new version is analysed against, and turns its analysis into a version pull request.</summary>
internal static class VersionCommands
{
    public static Command Create(ToolContext context)
    {
        var version = new Command("version", "Prepare and propose versions.");
        version.Add(Previous(context));
        version.Add(Propose(context));
        return version;
    }

    private static Command Previous(ToolContext context)
    {
        var build = BuildOption();
        var registry = CommonOptions.Registry();
        var current = CommonOptions.Current();
        var output = CommonOptions.Output("The folder to download the previous version's package to.");
        var command = new Command("previous",
            "Download the package of the highest lower version the deployed index hosts, for rshub analyse --previous; the step output package names it.")
        {
            build, registry, current, output,
        };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var job = BuildJob.Read(result.GetRequiredValue(build));
            var catalog = await CatalogAsync(context, result.GetRequiredValue(registry), result.GetRequiredValue(current), cancellationToken).ConfigureAwait(false);
            var version = SemanticVersion.TryParse(job.Version, out var parsed) ? parsed : throw new ToolException($"\"{job.Version}\" is not a version.");
            var path = await new PreviousVersionDownloader(context.Services.Downloads())
                .DownloadAsync(catalog, job.Plugin, version, result.GetRequiredValue(output), cancellationToken).ConfigureAwait(false);
            ActionsOutput.Set(context, "package", path ?? "");
            await writer.WriteLineAsync(path is null ? $"{job.Plugin} has no earlier published version." : $"Downloaded {Path.GetFileName(path)}.").ConfigureAwait(false);
            return 0;
        }));
        return command;
    }

    private static Command Propose(ToolContext context)
    {
        var analysis = new Option<string>("--analysis") { Description = "The folder with analysis.json and summary.md.", Required = true };
        var build = BuildOption();
        var package = new Option<string>("--package") { Description = "The folder rshub build package wrote.", Required = true };
        var runId = new Option<long?>("--run-id") { Description = "The workflow run that built the version; GITHUB_RUN_ID by default." };
        var registry = CommonOptions.Registry();
        var current = CommonOptions.Current();
        var command = new Command("propose",
            "Write the version record and open the version pull request, or reject the version on its tracking issue when the analysis or the checks against the hub fail.")
        {
            analysis, build, package, runId, registry, current,
        };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var folder = result.GetRequiredValue(analysis);
            var analysisBytes = await File.ReadAllBytesAsync(Path.Combine(folder, AnalysisReport.FileName), cancellationToken).ConfigureAwait(false);
            var summaryPath = Path.Combine(folder, SummaryWriter.FileName);
            var summary = File.Exists(summaryPath) ? await File.ReadAllTextAsync(summaryPath, cancellationToken).ConfigureAwait(false) : "";
            var job = BuildJob.Read(result.GetRequiredValue(build));
            var run = result.GetValue(runId) ?? (long.TryParse(context.GetEnvironmentVariable("GITHUB_RUN_ID"), CultureInfo.InvariantCulture, out var id)
                ? id
                : throw new ToolException("Give --run-id or set GITHUB_RUN_ID."));

            var directory = result.GetRequiredValue(registry);
            var inputs = VersionInputs.Read(analysisBytes, job, result.GetRequiredValue(package), run);
            var proposer = new VersionProposer(context.Services.Bot());
            ProposalResult proposal;
            if (inputs.Analysis.Verdict == AnalysisOutcome.Failed)
            {
                proposal = await proposer.RejectFailedAsync(job, inputs.Analysis, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var state = await new RegistryReader(new DirectoryRegistryFiles(directory)).LoadAsync(cancellationToken).ConfigureAwait(false);
                var catalog = await CatalogAsync(context, directory, result.GetRequiredValue(current), cancellationToken).ConfigureAwait(false);
                proposal = await proposer.ProposeAsync(inputs, summary, state, new GitRegistryHistory(context.Services.Git(directory), "HEAD"), catalog, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (proposal.PullRequest is { } number)
                ActionsOutput.Set(context, "pull-request", number.ToString(CultureInfo.InvariantCulture));
            await writer.WriteLineAsync(proposal.Message).ConfigureAwait(false);
            return 0;
        }));
        return command;
    }

    private static Option<string> BuildOption() => new("--build") { Description = "The build job, build-info.json.", Required = true };

    /// <summary>Loads the deployed index, verified from the registry's roots; an empty catalog when nothing is deployed yet.</summary>
    private static async Task<Protocol.Catalog.HubCatalog> CatalogAsync(ToolContext context, string registry, string current, CancellationToken cancellationToken)
    {
        var reader = new RegistryReader(new DirectoryRegistryFiles(registry));
        var trust = TrustRoot.Verify(await reader.RootsAsync(cancellationToken).ConfigureAwait(false));
        var deployed = await DeployedIndex.LoadAsync(context.Services.Index(current), trust, context.Time, cancellationToken).ConfigureAwait(false);
        return (deployed ?? IndexState.Empty).Catalog();
    }
}
