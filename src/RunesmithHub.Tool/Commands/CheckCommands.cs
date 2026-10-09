using System.CommandLine;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Analysis.Vulnerabilities;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Check;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.NuGet;
using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub check</c>: the hub's build and analysis, run by developers before they release.</summary>
internal static class CheckCommands
{
    public static Command Create(ToolContext context)
    {
        var path = new Argument<string>("path") { Description = "The plugin's folder, with plugin.json.", DefaultValueFactory = _ => "." };
        var output = new Option<string>("--output") { Description = "The folder for the build files, package and report; obj/hub-check in the plugin by default." };
        var noBuild = new Option<bool>("--no-build") { Description = "Analyse the existing Release build instead of restoring and building." };
        var cache = new Option<string>("--package-cache") { Description = "A folder of packages to read bundled packages from; NuGet's global packages folder by default." };
        var feed = BuildCommands.FeedOption();
        var command = new Command("check", "Build, package and analyse a plugin the way the hub does, and print the report; exit code 1 when it fails.")
        {
            path, output, noBuild, cache, feed,
        };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var root = Path.GetFullPath(result.GetRequiredValue(path));
            var request = new CheckRequest(
                root,
                result.GetValue(output) ?? Path.Combine(root, "obj", "hub-check"),
                !result.GetValue(noBuild),
                NuGetFeeds.HubFeed(result.GetValue(feed), context.GetEnvironmentVariable));
            var packages = BuildCommands.PackageSource(context, result.GetValue(feed), result.GetValue(cache) ?? GlobalPackagesFolder(context));
            var assembler = new PackageAssembler(new BundleVerifier(packages), new SourceArchiver(new GitCheckout(context.Services.Processes)));
            var check = new DeveloperCheck(context.Services.Processes, assembler, new PluginAnalyzer(new OsvDatabase(context.Services.Http()), packages, context.Time), context.Time, writer);
            var analysis = await check.RunAsync(request, cancellationToken).ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.WriteAsync(SummaryWriter.Write(analysis.Report)).ConfigureAwait(false);
            return analysis.Report.Verdict == AnalysisOutcome.Failed ? 1 : 0;
        }));
        return command;
    }

    private static string GlobalPackagesFolder(ToolContext context) =>
        context.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } folder
            ? folder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
}
