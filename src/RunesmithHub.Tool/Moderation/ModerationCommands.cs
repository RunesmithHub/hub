using System.Globalization;
using System.Text;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Gates;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Registry;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Moderation;

/// <summary>What a command did: the reply posted on the issue, and the pull request whose gates should run again.</summary>
internal sealed record CommandOutcome(string Reply, int? PullRequest);

/// <summary>Authorises administrator commands by <c>admins</c> team membership and turns them into bot pull requests and replies.</summary>
internal sealed class ModerationCommands(IGitHubReader github, IGitHubWriter bot, TimeProvider time)
{
    public async Task<CommandOutcome?> RunAsync(string comment, long actorId, int issue, RegistryState registry, CancellationToken cancellationToken)
    {
        AdminCommand? command;
        try
        {
            command = CommandParser.Parse(comment);
        }
        catch (ToolException exception)
        {
            return await ReplyAsync(issue, $"{exception.Message}\n\n{CommandParser.Usage}", null, cancellationToken).ConfigureAwait(false);
        }

        if (command is null)
            return null;

        var admins = await github.GetTeamMemberIdsAsync(HubLocations.Organization, HubLocations.AdminsTeam, cancellationToken).ConfigureAwait(false);
        if (!admins.Contains(actorId))
            return await ReplyAsync(issue, "Only administrators can use hub commands.", null, cancellationToken).ConfigureAwait(false);
        var user = await github.GetUserAsync(actorId, cancellationToken).ConfigureAwait(false) ?? throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"User {actorId} does not exist."));
        var actor = new GitHubAccount { Id = user.Id, Login = user.Login };

        try
        {
            return command.Verb switch
            {
                CommandVerb.Approve or CommandVerb.Reject or CommandVerb.Review => await OnVersionAsync(command, actor, issue, registry, cancellationToken).ConfigureAwait(false),
                CommandVerb.Advisory => await AdvisoryAsync(command, actor, issue, cancellationToken).ConfigureAwait(false),
                _ => await ProposeAsync(command, actor, issue, registry, admins.Count, cancellationToken).ConfigureAwait(false),
            };
        }
        catch (ToolException exception)
        {
            return await ReplyAsync(issue, exception.Message, null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<CommandOutcome> OnVersionAsync(AdminCommand command, GitHubAccount actor, int issue, RegistryState registry, CancellationToken cancellationToken)
    {
        var tracking = await github.GetIssueAsync(HubLocations.Registry, issue, cancellationToken).ConfigureAwait(false);
        if (tracking is null || !tracking.Labels.Contains(HubLabels.Tracking) || !TrackingIssues.TryParseTitle(tracking.Title, out var pluginId, out var version))
            throw new ToolException($"/{command.Verb.ToString().ToLowerInvariant()} works only on a version's tracking issue.");
        var pull = await github.FindPullRequestAsync(HubLocations.Registry, TrackingIssues.Branch(pluginId, version), cancellationToken).ConfigureAwait(false);
        if (pull is not { Open: true })
            throw new ToolException($"{pluginId} {version} has no open version pull request.");

        switch (command.Verb)
        {
            case CommandVerb.Approve:
                await bot.CommentAsync(HubLocations.Registry, pull.Number, Approvals.Marker(actor, pull.HeadSha), cancellationToken).ConfigureAwait(false);
                return await ReplyAsync(issue, Invariant($"Approved by @{actor.Login}; #{pull.Number} merges when its checks pass."), pull.Number, cancellationToken).ConfigureAwait(false);

            case CommandVerb.Reject:
                await bot.ClosePullRequestAsync(HubLocations.Registry, pull.Number, cancellationToken).ConfigureAwait(false);
                await new VersionProposer(bot).RejectAsync(issue, command.Note, [Invariant($"Rejected by @{actor.Login}.")], cancellationToken).ConfigureAwait(false);
                return new CommandOutcome("Rejected.", null);

            default:
                var path = RegistryPaths.Version(pluginId, version);
                var bytes = await github.GetFileAsync(HubLocations.Registry, path, pull.HeadSha, cancellationToken).ConfigureAwait(false)
                    ?? throw new ToolException($"#{pull.Number} has no {path}.");
                var record = RegistryReader.Parse<RegistryVersionRecord>(bytes, path);
                if (record.State != VersionState.Held)
                    throw new ToolException($"{pluginId} {version} is not held, so there is nothing to move to review.");
                await bot.UpdateBranchAsync(HubLocations.Registry, pull.HeadRef, $"Move {pluginId} {version} to review",
                    Files((path, RegistryJson.Serialize(record with { State = VersionState.Review, HoldUntil = null }))), cancellationToken).ConfigureAwait(false);
                return await ReplyAsync(issue, Invariant($"{pluginId} {version} now needs an administrator's review (#{pull.Number})."), pull.Number, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<CommandOutcome> AdvisoryAsync(AdminCommand command, GitHubAccount actor, int issue, CancellationToken cancellationToken)
    {
        var url = await bot.DraftAdvisoryAsync(HubLocations.Registry, $"Security advisory for {command.Target}",
            Invariant($"Drafted by @{actor.Login} from #{issue} for `{command.Target}`. Describe the affected versions, the impact and what users should do before publishing."),
            cancellationToken).ConfigureAwait(false);
        return await ReplyAsync(issue, $"Drafted a security advisory: {url}", null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandOutcome> ProposeAsync(AdminCommand command, GitHubAccount actor, int issue, RegistryState registry, int adminCount, CancellationToken cancellationToken)
    {
        var now = UtcTimestampConverter.Truncate(time.GetUtcNow());
        var change = RecordChanges.For(command, actor, now, registry);
        var strict = change.Strict;
        var body = new StringBuilder()
            .Append(Invariant($"Requested by @{actor.Login} in #{issue}:\n\n```\n{command.Text}\n```\n\n"))
            .AppendJoin('\n', change.Summary.Select(line => "- " + line)).Append('\n');
        if (strict)
            body.Append("\nThis loosens a restriction, so it follows the strict path: an owner approves it").Append(adminCount < 2 ? ", and it waits 24 hours" : "").Append(".\n");

        var labels = strict ? new[] { HubLabels.Moderation, HubLabels.StrictPath } : [HubLabels.Moderation];
        var number = await bot.OpenPullRequestAsync(HubLocations.Registry,
            new ChangeSet(change.Branch, change.Title, body.ToString(), change.Files, labels, AutoMerge: true), cancellationToken).ConfigureAwait(false);
        var reply = strict
            ? Invariant($"Opened #{number}. It follows the strict path: an owner approves it{(adminCount < 2 ? " and it waits 24 hours" : "")}.")
            : Invariant($"Opened #{number}. It merges when an administrator approves it, and the next moderation and publish runs apply it.");
        return await ReplyAsync(issue, reply, number, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandOutcome> ReplyAsync(int issue, string text, int? pullRequest, CancellationToken cancellationToken)
    {
        await bot.CommentAsync(HubLocations.Registry, issue, text, cancellationToken).ConfigureAwait(false);
        return new CommandOutcome(text, pullRequest);
    }

    private static Dictionary<string, byte[]?> Files(params (string Path, byte[] Bytes)[] files) =>
        files.ToDictionary(file => file.Path, file => (byte[]?)file.Bytes, StringComparer.Ordinal);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The registry files a moderation command changes.</summary>
internal sealed record RecordChange(string Branch, string Title, IReadOnlyDictionary<string, byte[]?> Files, IReadOnlyList<string> Summary, bool Strict);

/// <summary>Turns a moderation command into the moderation entry and record states it changes.</summary>
internal static class RecordChanges
{
    private const string DefaultFreezeNote = "Installs are paused while Runesmith looks into a report.";

    /// <exception cref="ToolException">The target does not exist or the action does not apply to it.</exception>
    public static RecordChange For(AdminCommand command, GitHubAccount actor, DateTimeOffset now, RegistryState registry)
    {
        var files = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var summary = new List<string>();
        var target = command.Target!;
        var strict = false;
        var verb = command.Verb.ToString().ToLowerInvariant();
        string? branch = null;

        switch (command.Verb)
        {
            case CommandVerb.Freeze or CommandVerb.Block:
            {
                var block = command.Verb == CommandVerb.Block;
                var id = registry.Moderation.Select(entry => entry.Id).DefaultIfEmpty(0).Max() + 1;
                var entry = new RegistryModerationEntry
                {
                    Id = id,
                    Target = target,
                    Action = block ? ModerationAction.Block : ModerationAction.Freeze,
                    Reason = command.Reason!.Value,
                    Note = command.Note.Length > 0 ? command.Note : block ? $"Blocked: {Reasons.Text(command.Reason.Value)}." : DefaultFreezeNote,
                    CreatedAt = now,
                    CreatedBy = actor.Id,
                };
                files[RegistryPaths.ModerationEntry(id)] = RegistryJson.Serialize(entry);
                summary.Add(Invariant($"Adds moderation entry {id}: {verb} `{target}` ({Reasons.Text(entry.Reason)})."));
                ApplyState(target, registry, block ? VersionState.Blocked : VersionState.Frozen, files, summary, onlyLoosening: false);
                branch = Invariant($"moderation/{id:D5}");
                break;
            }

            case CommandVerb.Unfreeze or CommandVerb.Unblock:
            {
                var action = command.Verb == CommandVerb.Unblock ? ModerationAction.Block : ModerationAction.Freeze;
                var lifted = registry.ActiveModeration.Where(entry => entry.Action == action && string.Equals(entry.Target.ToString(), target.ToString(), StringComparison.Ordinal)).ToList();
                if (lifted.Count == 0)
                    throw new ToolException($"There is no active {action.ToString().ToLowerInvariant()} of `{target}`.");
                foreach (var entry in lifted)
                {
                    files[RegistryPaths.ModerationEntry(entry.Id)] = RegistryJson.Serialize(entry with { LiftedAt = now, LiftedBy = actor.Id });
                    summary.Add(Invariant($"Lifts moderation entry {entry.Id}."));
                    strict |= action == ModerationAction.Block || StrictPath.IsSecurityRelated(entry.Reason);
                }

                ApplyState(target, registry, VersionState.Published, files, summary, onlyLoosening: true, from: action == ModerationAction.Block ? VersionState.Blocked : VersionState.Frozen);
                branch = Invariant($"moderation/lift-{lifted[0].Id:D5}");
                break;
            }

            case CommandVerb.Delete:
                if (target.Kind == ModerationTargetKind.Publisher)
                    throw new ToolException("Publishers are suspended or blocked, not deleted.");
                ApplyState(target, registry, VersionState.Deleted, files, summary, onlyLoosening: false);
                break;

            case CommandVerb.Verify or CommandVerb.Unverify:
            {
                var registration = Registration(registry, target.Plugin!);
                var tier = command.Verb == CommandVerb.Verify ? PluginTier.Verified : PluginTier.Unverified;
                if (registration.Tier == PluginTier.Official || registration.Tier == tier)
                    throw new ToolException($"{registration.Id} is {registration.Tier.ToString().ToLowerInvariant()} already.");
                files[RegistryPaths.Registration(registration.Id)] = RegistryJson.Serialize(registration with { Tier = tier });
                summary.Add($"Makes {registration.Id} {tier.ToString().ToLowerInvariant()}.");
                if (command.Verb == CommandVerb.Verify)
                {
                    strict = true;
                    var publisher = Publisher(registry, PluginId.Parse(registration.Id).Publisher);
                    if (publisher.Verified is null)
                    {
                        files[RegistryPaths.Publisher(publisher.Name)] = RegistryJson.Serialize(publisher with { Verified = new PublisherVerification { At = now, By = actor.Id } });
                        summary.Add($"Verifies the publisher {publisher.Name}.");
                    }
                }

                break;
            }

            default:
            {
                if (target.Kind != ModerationTargetKind.Publisher)
                    throw new ToolException($"/{verb} needs `publisher:<name>`.");
                var publisher = Publisher(registry, target.Publisher!);
                var (from, to) = command.Verb == CommandVerb.Suspend ? (PublisherState.Active, PublisherState.Suspended) : (PublisherState.Suspended, PublisherState.Active);
                if (publisher.State != from)
                    throw new ToolException($"The publisher {publisher.Name} is {publisher.State.ToString().ToLowerInvariant()}.");
                files[RegistryPaths.Publisher(publisher.Name)] = RegistryJson.Serialize(publisher with { State = to });
                summary.Add($"Makes the publisher {publisher.Name} {to.ToString().ToLowerInvariant()}.");
                strict = command.Verb == CommandVerb.Unsuspend;
                break;
            }
        }

        if (files.Count == 0)
            throw new ToolException($"`{command.Text}` changes nothing.");
        var title = $"{char.ToUpperInvariant(verb[0])}{verb[1..]} {target}" + (command.Reason is { } reason ? $" ({Reasons.Text(reason)})" : "");
        return new RecordChange(branch ?? $"moderation/{verb}-{Slug(target.ToString())}-{now:yyyyMMddHHmmss}", title, files, summary, strict);
    }

    /// <summary>Sets the state of the records a target covers.</summary>
    /// <param name="onlyLoosening">Changes only records in <paramref name="from"/>, for lifting a freeze or block.</param>
    private static void ApplyState(
        ModerationTarget target, RegistryState registry, VersionState state, Dictionary<string, byte[]?> files, List<string> summary, bool onlyLoosening, VersionState? from = null)
    {
        switch (target.Kind)
        {
            case ModerationTargetKind.Publisher:
            {
                var publisher = Publisher(registry, target.Publisher!);
                PublisherState? next = (state, publisher.State, onlyLoosening) switch
                {
                    (VersionState.Blocked, not PublisherState.Blocked, false) => PublisherState.Blocked,
                    (VersionState.Published, PublisherState.Blocked, true) when from == VersionState.Blocked => PublisherState.Active,
                    _ => null,
                };
                if (next is { } publisherState)
                {
                    files[RegistryPaths.Publisher(publisher.Name)] = RegistryJson.Serialize(publisher with { State = publisherState });
                    summary.Add($"Makes the publisher {publisher.Name} {publisherState.ToString().ToLowerInvariant()}.");
                }

                break;
            }

            case ModerationTargetKind.Plugin:
            {
                var registration = Registration(registry, target.Plugin!);
                var current = registration.State;
                RegistrationState? next = onlyLoosening
                    ? current == (from == VersionState.Blocked ? RegistrationState.Blocked : RegistrationState.Frozen) ? RegistrationState.Listed : null
                    : state switch
                    {
                        VersionState.Blocked when current != RegistrationState.Blocked => RegistrationState.Blocked,
                        VersionState.Frozen when current is not (RegistrationState.Frozen or RegistrationState.Blocked or RegistrationState.Deleted) => RegistrationState.Frozen,
                        VersionState.Deleted when current != RegistrationState.Deleted => RegistrationState.Deleted,
                        _ => null,
                    };
                if (next is { } registrationState)
                {
                    files[RegistryPaths.Registration(registration.Id)] = RegistryJson.Serialize(registration with { State = registrationState });
                    summary.Add($"Makes {registration.Id} {registrationState.ToString().ToLowerInvariant()}.");
                }

                break;
            }

            default:
            {
                Registration(registry, target.Plugin!);
                var covered = registry.VersionsOf(target.Plugin!).Where(record => target.Range!.Contains(record.Version, allowPreRelease: true)).ToList();
                if (covered.Count == 0)
                    throw new ToolException($"No version of {target.Plugin} is in `{target.Range}`.");
                foreach (var record in covered)
                {
                    var change = onlyLoosening
                        ? record.State == from
                        : Protocol.Catalog.EffectiveState.Restrictiveness(state) > Protocol.Catalog.EffectiveState.Restrictiveness(record.State);
                    if (!change)
                        continue;
                    files[RegistryPaths.Version(target.Plugin!, record.Version)] = RegistryJson.Serialize(record with { State = state });
                    summary.Add($"Makes {target.Plugin} {record.Version} {state.ToString().ToLowerInvariant()}.");
                }

                break;
            }
        }
    }

    private static Registration Registration(RegistryState registry, string id) =>
        registry.FindRegistration(id) ?? throw new ToolException($"{id} is not registered.");

    private static PublisherFile Publisher(RegistryState registry, string name) =>
        registry.FindPublisher(name) ?? throw new ToolException($"The publisher {name} is not registered.");

    private static string Slug(string text) => new([.. text.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '-')]);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The words for moderation reasons, as administrators write them.</summary>
internal static class Reasons
{
    public static string Text(ModerationReason reason) => reason switch
    {
        ModerationReason.CompromisedAccount => "compromised account",
        ModerationReason.DeveloperRequest => "developer request",
        _ => reason.ToString().ToLowerInvariant(),
    };
}
