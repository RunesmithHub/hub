using System.CommandLine;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Tool.Dashboard;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Json;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub dashboard</c>: writes a fresh <c>dashboard.json</c> into the index directory, which needs no key.</summary>
internal static class DashboardCommand
{
    public static Command Create(ToolContext context)
    {
        var registry = CommonOptions.Registry();
        var index = CommonOptions.Index();
        var command = new Command("dashboard", "Build dashboard.json from the registry and GitHub and write it into the index directory.") { registry, index };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var reader = new RegistryReader(new DirectoryRegistryFiles(result.GetRequiredValue(registry)));
            var trust = TrustRoot.Verify(await reader.RootsAsync(cancellationToken).ConfigureAwait(false));
            var directory = result.GetRequiredValue(index);
            var current = IndexState.Read(directory, trust) ?? IndexState.Empty;
            var summary = await new DashboardBuilder(context.Services.GitHub(), context.Time).BuildAsync(await reader.LoadAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            var state = current.With(new Dictionary<string, byte[]>(StringComparer.Ordinal) { [IndexPaths.Dashboard] = ToolJson.Serialize(summary) });
            IndexOutput.Write(directory, trust, state);
            await writer.WriteLineAsync($"Dashboard: {summary.ReviewQueue.Count} in review, {summary.Held.Count} held, {summary.AuditFlags.Count} audit flags.").ConfigureAwait(false);
            return 0;
        }));
        return command;
    }
}
