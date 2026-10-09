using System.CommandLine;
using System.Globalization;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Tool.Registrations;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub registration</c>: the registration check of registry pull requests.</summary>
internal static class RegistrationCommands
{
    public static Command Create(ToolContext context)
    {
        var registration = new Command("registration", "Check registrations.");
        registration.Add(Validate(context));
        return registration;
    }

    private static Command Validate(ToolContext context)
    {
        var baseRef = new Option<string>("--base") { Description = "The Git ref the pull request merges into, such as origin/main.", Required = true };
        var head = new Option<string>("--head") { Description = "The checked-out pull request.", DefaultValueFactory = _ => "." };
        var command = new Command("validate",
            "Validate the changed publisher and registration files. The pull request's author comes from HUB_PR_AUTHOR_ID and HUB_PR_AUTHOR_LOGIN.") { baseRef, head };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var directory = result.GetRequiredValue(head);
            var authorId = long.TryParse(context.RequireEnvironmentVariable("HUB_PR_AUTHOR_ID"), CultureInfo.InvariantCulture, out var id)
                ? id
                : throw new ToolException("HUB_PR_AUTHOR_ID is not a number.");
            var author = new GitHubAccount { Id = authorId, Login = context.RequireEnvironmentVariable("HUB_PR_AUTHOR_LOGIN") };
            var git = context.Services.Git(directory);
            var changes = await git.DiffAsync(result.GetRequiredValue(baseRef), "HEAD", cancellationToken).ConfigureAwait(false);
            var report = await new RegistrationValidator(context.Services.GitHub()).ValidateAsync(
                new DirectoryRegistryFiles(directory), new GitRegistryFiles(git, result.GetRequiredValue(baseRef)), changes, author, cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync(report.Render()).ConfigureAwait(false);
            return report.Failed ? 1 : 0;
        }));
        return command;
    }
}
