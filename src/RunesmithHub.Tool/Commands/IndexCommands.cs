using System.CommandLine;
using RunesmithHub.Tool.IndexBuild;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub index</c>: the signing jobs' index builds.</summary>
internal static class IndexCommands
{
    public static Command Create(ToolContext context)
    {
        var index = new Command("index", "Build and sign the index in an index directory. Keys come from HUB_PUBLISH_KEY, HUB_MODERATION_KEY and HUB_TIMESTAMP_KEY.");
        index.Add(Create(context, IndexOperation.Publish,
            "Write the plugin records, publisher list and policy from the registry and sign a new snapshot with HUB_PUBLISH_KEY."));
        index.Add(Create(context, IndexOperation.Moderate, "Sign a new moderation list of the active moderation entries with HUB_MODERATION_KEY."));
        index.Add(Create(context, IndexOperation.Timestamp, "Sign a timestamp for the newest snapshot and moderation list with HUB_TIMESTAMP_KEY."));
        index.Add(Create(context, IndexOperation.Refresh, "Sign a fresh timestamp with HUB_TIMESTAMP_KEY for an index that already has one."));
        return index;
    }

    private static Command Create(ToolContext context, IndexOperation operation, string description)
    {
        var registry = CommonOptions.Registry();
        var index = CommonOptions.Index();
        var command = new Command(operation.ToString().ToLowerInvariant(), description) { registry, index };
        var publications = new Option<string>("--publications") { Description = "The publications file from publish artifacts." };
        if (operation == IndexOperation.Publish)
            command.Add(publications);

        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var options = new IndexJobOptions(result.GetRequiredValue(registry), result.GetRequiredValue(index), result.GetValue(publications));
            var outcome = await new IndexJob(context).RunAsync(operation, options, cancellationToken).ConfigureAwait(false);
            ActionsOutput.Set(context, "bootstrap", outcome.Bootstrap ? "true" : "false");
            foreach (var note in outcome.Notes)
                await writer.WriteLineAsync(note).ConfigureAwait(false);
            return 0;
        }));
        return command;
    }
}
