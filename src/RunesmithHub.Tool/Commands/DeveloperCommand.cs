using System.CommandLine;
using System.Globalization;
using RunesmithHub.Tool.Developer;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub developer</c>: a maintainer's request from an issue form.</summary>
internal static class DeveloperCommand
{
    public static Command Create(ToolContext context)
    {
        var issue = new Option<string>("--issue") { Description = "A file with the issue object of the issues event.", Required = true };
        var author = new Option<long>("--author-id") { Description = "The issue author's permanent GitHub user ID.", Required = true };
        var registry = CommonOptions.Registry();
        var command = new Command("developer", "Carry out a maintainer's request: publish now, yank, un-yank, deprecate, undeprecate, maintainers, verification, delete or transfer.")
        {
            issue, author, registry,
        };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var form = IssueForm.Read(await File.ReadAllBytesAsync(result.GetRequiredValue(issue), cancellationToken).ConfigureAwait(false));
            if (form.AuthorId != result.GetRequiredValue(author))
                throw new ToolException("--author-id does not match the issue's author.");
            var state = await new RegistryReader(new DirectoryRegistryFiles(result.GetRequiredValue(registry))).LoadAsync(cancellationToken).ConfigureAwait(false);
            var outcome = await new DeveloperActions(context.Services.GitHub(), context.Services.Bot(), context.Time).RunAsync(form, state, cancellationToken).ConfigureAwait(false);
            if (outcome.PublishNow is { } plugin)
                ActionsOutput.Set(context, "plugin", plugin);
            if (outcome.PullRequest is { } number)
                ActionsOutput.Set(context, "pull-request", number.ToString(CultureInfo.InvariantCulture));
            await writer.WriteLineAsync(outcome.Reply).ConfigureAwait(false);
            return 0;
        }));
        return command;
    }
}
