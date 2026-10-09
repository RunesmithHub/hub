using System.CommandLine;
using RunesmithHub.Tool.Gates;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub gate</c>: the three required checks, reported as commit statuses on the pull request's head.</summary>
internal static class GateCommands
{
    private enum Gate
    {
        Verify,
        Review,
        Hold,
    }

    public static Command Create(ToolContext context)
    {
        var gate = new Command("gate", "Run a required check on a registry pull request and set its commit status.");
        gate.Add(Create(context, Gate.Verify, "Check every new version record is exactly what the pipeline computes, and edits change only states."));
        gate.Add(Create(context, Gate.Review, "Check versions, loosened states and strict-path changes have the approvals they need."));
        gate.Add(Create(context, Gate.Hold, "Check held versions have waited out their hold."));
        return gate;
    }

    private static Command Create(ToolContext context, Gate which, string description)
    {
        var pr = CommonOptions.PullRequest();
        var registry = CommonOptions.Registry();
        var defaultBranch = new Option<string>("--default-branch") { Description = "The Git ref of the default branch.", DefaultValueFactory = _ => "origin/main" };
        var command = new Command(which.ToString().ToLowerInvariant(), description) { pr, registry, defaultBranch };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var github = context.Services.GitHub();
            var bot = context.Services.Bot();
            var git = context.Services.Git(result.GetRequiredValue(registry));
            var inputs = await VersionGates.LoadAsync(github, git, result.GetRequiredValue(pr), result.GetRequiredValue(defaultBranch), cancellationToken).ConfigureAwait(false);
            var gates = new VersionGates(github, context.Services.Artifacts(), git, context.Time, CommonOptions.BotLogin(context));
            var outcome = which switch
            {
                Gate.Verify => await gates.VerifyAsync(inputs, cancellationToken).ConfigureAwait(false),
                Gate.Review => await gates.ReviewAsync(inputs, cancellationToken).ConfigureAwait(false),
                _ => gates.Hold(inputs),
            };

            await bot.SetStatusAsync(HubLocations.Registry, inputs.Pull.HeadSha, outcome.Context, outcome.State, outcome.Description, cancellationToken).ConfigureAwait(false);
            if (outcome.Rejection is { } rejection)
                await RejectAsync(bot, inputs, rejection, cancellationToken).ConfigureAwait(false);
            foreach (var line in outcome.Lines)
                await writer.WriteLineAsync($"{outcome.Context}: {line}").ConfigureAwait(false);
            return outcome.State == CommitStatusState.Failure ? 1 : 0;
        }));
        return command;
    }

    /// <summary>Closes a version pull request an administrator requested changes on, and sends the review to the tracking issue.</summary>
    private static async Task RejectAsync(IGitHubWriter bot, GateInputs inputs, PullRequestReview review, CancellationToken cancellationToken)
    {
        await bot.ClosePullRequestAsync(HubLocations.Registry, inputs.Pull.Number, cancellationToken).ConfigureAwait(false);
        foreach (var record in inputs.Changes.Versions.Select(change => change.After).OfType<Protocol.Registry.RegistryVersionRecord>())
        {
            var reason = review.Body.Trim().Length > 0 ? review.Body.Trim() : "an administrator requested changes.";
            await new VersionProposer(bot).RejectAsync(record.TrackingIssue, reason, [$"Requested by @{review.Author.Login} on #{inputs.Pull.Number}."], cancellationToken).ConfigureAwait(false);
        }
    }
}
