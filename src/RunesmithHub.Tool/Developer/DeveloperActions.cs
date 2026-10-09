using System.Globalization;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Audit;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Developer;

/// <summary>What a developer action did.</summary>
/// <param name="PublishNow">The plugin to run discovery for, for a publish-now request.</param>
internal sealed record DeveloperOutcome(string Reply, int? PullRequest, string? PublishNow);

/// <summary>
/// Carries out what maintainers ask for through the registry's issue forms, after checking by permanent user ID that the issue's author
/// maintains the plugin. Changes become pull requests the bot opens; the ones that need an administrator wait for one.
/// </summary>
internal sealed class DeveloperActions(IGitHubReader github, IGitHubWriter bot, TimeProvider time)
{
    /// <summary>The issue form labels, one per action.</summary>
    public static IReadOnlyList<string> Labels { get; } = ["publish-now", "yank", "unyank", "deprecate", "undeprecate", "maintainers", "verification", "delete", "transfer"];

    public async Task<DeveloperOutcome> RunAsync(IssueForm issue, RegistryState registry, CancellationToken cancellationToken)
    {
        var action = issue.Labels.FirstOrDefault(Labels.Contains);
        var pluginId = issue.Field("Plugin ID");
        var registration = action is null ? null : registry.FindRegistration(pluginId);
        string? problem = (action, registration) switch
        {
            (null, _) => "This issue is not a developer action.",
            (_, null) => $"`{pluginId}` is not a registered plugin. Check the plugin ID.",
            _ when !registration.Maintainers.Any(maintainer => maintainer.Id == issue.AuthorId) =>
                $"Only the maintainers of {registration.Id} can ask for this. To change the maintainers, a current maintainer opens a Maintainers issue.",
            _ => null,
        };
        if (problem is not null)
        {
            await ReplyAsync(issue, problem, close: true, cancellationToken).ConfigureAwait(false);
            return new DeveloperOutcome(problem, null, null);
        }

        try
        {
            return action switch
            {
                "publish-now" => await PublishNowAsync(issue, registration!, cancellationToken).ConfigureAwait(false),
                "yank" or "unyank" => await YankAsync(issue, registration!, registry, action == "yank", cancellationToken).ConfigureAwait(false),
                "deprecate" or "undeprecate" => await DeprecateAsync(issue, registration!, registry, action == "deprecate", cancellationToken).ConfigureAwait(false),
                "maintainers" => await MaintainersAsync(issue, registration!, cancellationToken).ConfigureAwait(false),
                "verification" => await VerificationAsync(issue, registration!, registry, cancellationToken).ConfigureAwait(false),
                "delete" => await DeleteAsync(issue, registration!, cancellationToken).ConfigureAwait(false),
                _ => await TransferAsync(issue, registration!, registry, cancellationToken).ConfigureAwait(false),
            };
        }
        catch (ToolException exception)
        {
            await ReplyAsync(issue, exception.Message, close: false, cancellationToken).ConfigureAwait(false);
            return new DeveloperOutcome(exception.Message, null, null);
        }
    }

    private async Task<DeveloperOutcome> PublishNowAsync(IssueForm issue, Registration registration, CancellationToken cancellationToken)
    {
        var reply = $"Looking for new releases of {registration.Id} now. A tracking issue opens for each release found.";
        await ReplyAsync(issue, reply, close: true, cancellationToken).ConfigureAwait(false);
        return new DeveloperOutcome(reply, null, registration.Id);
    }

    private async Task<DeveloperOutcome> YankAsync(IssueForm issue, Registration registration, RegistryState registry, bool yank, CancellationToken cancellationToken)
    {
        var text = issue.Field("Version");
        var version = SemanticVersion.TryParse(text, out var parsed) ? parsed : throw new ToolException($"`{text}` is not a version.");
        var record = registry.VersionsOf(registration.Id).FirstOrDefault(candidate => candidate.Version == version)
            ?? throw new ToolException($"{registration.Id} has no version {version}.");

        if (yank)
        {
            var reason = issue.Field("Reason");
            if (reason.Length is 0 or > 200)
                throw new ToolException("Give a reason of at most 200 characters for yanking.");
            if (record.State is not (VersionState.Published or VersionState.Held or VersionState.Review))
                throw new ToolException($"{registration.Id} {version} is {Name(record.State)}, so it cannot be yanked.");
            return await ProposeAsync(issue, $"Yank {registration.Id} {version}",
                Change(RegistryPaths.Version(registration.Id, version), RegistryJson.Serialize(record with { State = VersionState.Yanked, YankReason = reason })),
                "It merges once its checks pass.", cancellationToken).ConfigureAwait(false);
        }

        if (record.State != VersionState.Yanked)
            throw new ToolException($"{registration.Id} {version} is not yanked.");
        return await ProposeAsync(issue, $"Un-yank {registration.Id} {version}",
            Change(RegistryPaths.Version(registration.Id, version), RegistryJson.Serialize(record with { State = VersionState.Published, YankReason = null })),
            "An administrator approves it before it merges.", cancellationToken).ConfigureAwait(false);
    }

    private async Task<DeveloperOutcome> DeprecateAsync(IssueForm issue, Registration registration, RegistryState registry, bool deprecate, CancellationToken cancellationToken)
    {
        Registration updated;
        if (deprecate)
        {
            var reason = issue.Field("Reason");
            var replacement = issue.Field("Replacement plugin ID");
            if (reason.Length is 0 or > 200)
                throw new ToolException("Give a reason of at most 200 characters for deprecating.");
            if (replacement.Length > 0 && (registry.FindRegistration(replacement) is null || replacement == registration.Id))
                throw new ToolException($"`{replacement}` is not another registered plugin.");
            if (registration.State is not (RegistrationState.Listed or RegistrationState.Registered or RegistrationState.Deprecated))
                throw new ToolException($"{registration.Id} is {registration.State.ToString().ToLowerInvariant()}.");
            updated = registration with
            {
                State = RegistrationState.Deprecated,
                Deprecated = new Deprecation { Reason = reason, Replacement = replacement.Length > 0 ? replacement : null },
            };
        }
        else
        {
            if (registration.State != RegistrationState.Deprecated)
                throw new ToolException($"{registration.Id} is not deprecated.");
            updated = registration with { State = RegistrationState.Listed, Deprecated = null };
        }

        return await ProposeAsync(issue, $"{(deprecate ? "Deprecate" : "Undeprecate")} {registration.Id}",
            Change(RegistryPaths.Registration(registration.Id), RegistryJson.Serialize(updated)), "An administrator merges it.", cancellationToken).ConfigureAwait(false);
    }

    private async Task<DeveloperOutcome> MaintainersAsync(IssueForm issue, Registration registration, CancellationToken cancellationToken)
    {
        var maintainers = registration.Maintainers.ToList();
        foreach (var login in Logins(issue.Field("Remove maintainers")))
            maintainers.RemoveAll(maintainer => string.Equals(maintainer.Login, login, StringComparison.OrdinalIgnoreCase));
        foreach (var login in Logins(issue.Field("Add maintainers")))
        {
            var user = await github.GetUserAsync(login, cancellationToken).ConfigureAwait(false) ?? throw new ToolException($"@{login} is not a GitHub account.");
            if (maintainers.All(maintainer => maintainer.Id != user.Id))
                maintainers.Add(new GitHubAccount { Id = user.Id, Login = user.Login });
        }

        if (maintainers.Count == 0)
            throw new ToolException("A plugin needs at least one maintainer.");
        if (maintainers.Select(maintainer => maintainer.Id).ToHashSet().SetEquals(registration.Maintainers.Select(maintainer => maintainer.Id)))
            throw new ToolException("The maintainers would not change.");

        return await ProposeAsync(issue, $"Change the maintainers of {registration.Id}",
            Change(RegistryPaths.Registration(registration.Id), RegistryJson.Serialize(registration with { Maintainers = maintainers })),
            "An administrator approves it; as a current maintainer, your issue confirms it. The next version needs review.", cancellationToken).ConfigureAwait(false);
    }

    private async Task<DeveloperOutcome> VerificationAsync(IssueForm issue, Registration registration, RegistryState registry, CancellationToken cancellationToken)
    {
        if (registration.Tier != PluginTier.Unverified)
            throw new ToolException($"{registration.Id} is {registration.Tier.ToString().ToLowerInvariant()} already.");
        var publisher = registry.FindPublisher(PluginId.Parse(registration.Id).Publisher)!;
        var repository = await github.GetRepositoryAsync(registration.Repository.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new ToolException("The plugin's repository is not reachable.");
        var checks = await new VerifiedRequirements(github, time).CheckAsync(publisher, repository, cancellationToken).ConfigureAwait(false);
        var published = registry.VersionsOf(registration.Id).Any(record => record.State is VersionState.Published or VersionState.Held or VersionState.Review);
        var reply = $"""
            Verification request for {registration.Id}. @{HubLocations.Organization}/{HubLocations.AdminsTeam}, please review.

            {VerifiedRequirements.Checklist([new("The plugin has a published version that passed every automated check", published), .. checks])}
            - [ ] Release tags are protected and releases are immutable (an administrator checks this)
            - [ ] Two-factor authentication is required, or each maintainer confirms it here
            - [ ] The publisher accepts the publisher policy
            - [ ] An administrator has read the full source of the current version
            """;
        await bot.AddLabelsAsync(HubLocations.Registry, issue.Number, [HubLabels.Verification], cancellationToken).ConfigureAwait(false);
        await ReplyAsync(issue, reply, close: false, cancellationToken).ConfigureAwait(false);
        return new DeveloperOutcome(reply, null, null);
    }

    private async Task<DeveloperOutcome> DeleteAsync(IssueForm issue, Registration registration, CancellationToken cancellationToken)
    {
        if (registration.State == RegistrationState.Deleted)
            throw new ToolException($"{registration.Id} is deleted already.");
        return await ProposeAsync(issue, $"Delete {registration.Id}",
            Change(RegistryPaths.Registration(registration.Id), RegistryJson.Serialize(registration with { State = RegistrationState.Deleted })),
            "An administrator confirms it. The ID stays reserved for you.", cancellationToken).ConfigureAwait(false);
    }

    private async Task<DeveloperOutcome> TransferAsync(IssueForm issue, Registration registration, RegistryState registry, CancellationToken cancellationToken)
    {
        var target = issue.Field("New publisher");
        var publisher = registry.FindPublisher(target) ?? throw new ToolException($"`{target}` is not a registered publisher.");
        var reply = $"Transfer of {registration.Id} to {publisher.Name} requested. Both publishers confirm in this issue, then an administrator moves the " +
            $"registration. @{HubLocations.Organization}/{HubLocations.AdminsTeam}";
        await ReplyAsync(issue, reply, close: false, cancellationToken).ConfigureAwait(false);
        return new DeveloperOutcome(reply, null, null);
    }

    private async Task<DeveloperOutcome> ProposeAsync(IssueForm issue, string title, IReadOnlyDictionary<string, byte[]?> files, string next, CancellationToken cancellationToken)
    {
        var branch = string.Create(CultureInfo.InvariantCulture, $"developer/{issue.Number}-{time.GetUtcNow():yyyyMMddHHmmss}");
        var body = string.Create(CultureInfo.InvariantCulture, $"Requested by a maintainer in #{issue.Number}.\n\nCloses #{issue.Number}\n");
        var number = await bot.OpenPullRequestAsync(HubLocations.Registry, new ChangeSet(branch, title, body, files, [HubLabels.Developer], AutoMerge: true), cancellationToken).ConfigureAwait(false);
        var reply = string.Create(CultureInfo.InvariantCulture, $"Opened #{number}. {next}");
        await ReplyAsync(issue, reply, close: false, cancellationToken).ConfigureAwait(false);
        return new DeveloperOutcome(reply, number, null);
    }

    private async Task ReplyAsync(IssueForm issue, string text, bool close, CancellationToken cancellationToken)
    {
        await bot.CommentAsync(HubLocations.Registry, issue.Number, text, cancellationToken).ConfigureAwait(false);
        if (close)
            await bot.CloseIssueAsync(HubLocations.Registry, issue.Number, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<string> Logins(string field) =>
        field.Split(['\n', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(login => login.TrimStart('@')).Where(login => login.Length > 0);

    private static Dictionary<string, byte[]?> Change(string path, byte[] bytes) => new(StringComparer.Ordinal) { [path] = bytes };

    private static string Name(VersionState state) => state.ToString().ToLowerInvariant();
}
