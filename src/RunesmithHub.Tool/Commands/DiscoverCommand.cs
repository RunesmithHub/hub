using System.CommandLine;
using System.Globalization;
using RunesmithHub.Tool.Discovery;
using RunesmithHub.Tool.Json;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub discover</c>: finds new releases and emits the build matrix.</summary>
internal static class DiscoverCommand
{
    public static Command Create(ToolContext context)
    {
        var registry = CommonOptions.Registry();
        var plugin = new Option<string>("--plugin") { Description = "Look only at this plugin, for a publish-now request." };
        var output = new Option<string>("--out") { Description = "Where to write the build matrix as JSON; it is also set as the step output matrix." };
        var command = new Command("discover",
            "List new releases of registered repositories, open their tracking issues and emit build jobs. HUB_GITHUB_CACHE names the conditional-request cache file.")
        {
            registry, plugin, output,
        };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var state = await new RegistryReader(new DirectoryRegistryFiles(result.GetRequiredValue(registry))).LoadAsync(cancellationToken).ConfigureAwait(false);
            var discovery = new ReleaseDiscovery(context.Services.GitHub(), context.Services.Bot());
            var found = await discovery.DiscoverAsync(state, result.GetValue(plugin), cancellationToken).ConfigureAwait(false);
            context.Services.SaveCache();

            var matrix = System.Text.Encoding.UTF8.GetString(ToolJson.Serialize(new BuildMatrix(found.Jobs)));
            if (result.GetValue(output) is { } path)
                await File.WriteAllTextAsync(path, matrix, cancellationToken).ConfigureAwait(false);
            ActionsOutput.Set(context, "matrix", matrix);
            ActionsOutput.Set(context, "count", found.Jobs.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var note in found.Notes)
                await writer.WriteLineAsync(note).ConfigureAwait(false);
            await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{found.Jobs.Count} new release(s) to build.")).ConfigureAwait(false);
            return 0;
        }));
        return command;
    }
}
