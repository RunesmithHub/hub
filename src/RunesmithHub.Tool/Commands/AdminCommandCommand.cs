using System.CommandLine;
using System.Globalization;
using RunesmithHub.Tool.Moderation;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub command</c>: an administrator's command from an issue comment.</summary>
internal static class AdminCommandCommand
{
    public static Command Create(ToolContext context)
    {
        var comment = new Option<string>("--comment") { Description = "A file with the comment's text.", Required = true };
        var actor = new Option<long>("--actor-id") { Description = "The commenter's permanent GitHub user ID.", Required = true };
        var issue = new Option<int>("--issue") { Description = "The issue or pull request the comment is on.", Required = true };
        var registry = CommonOptions.Registry();
        var command = new Command("command",
            "Parse and authorise an administrator command, open the pull request for it with the bot, and reply on the issue.") { comment, actor, issue, registry };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var text = await File.ReadAllTextAsync(result.GetRequiredValue(comment), cancellationToken).ConfigureAwait(false);
            var state = await new RegistryReader(new DirectoryRegistryFiles(result.GetRequiredValue(registry))).LoadAsync(cancellationToken).ConfigureAwait(false);
            var outcome = await new ModerationCommands(context.Services.GitHub(), context.Services.Bot(), context.Time)
                .RunAsync(text, result.GetRequiredValue(actor), result.GetRequiredValue(issue), state, cancellationToken).ConfigureAwait(false);
            if (outcome is null)
            {
                await writer.WriteLineAsync("The comment has no command.").ConfigureAwait(false);
                return 0;
            }

            if (outcome.PullRequest is { } number)
                ActionsOutput.Set(context, "pull-request", number.ToString(CultureInfo.InvariantCulture));
            await writer.WriteLineAsync(outcome.Reply).ConfigureAwait(false);
            return 0;
        }));
        return command;
    }
}
