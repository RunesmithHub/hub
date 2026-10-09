using System.Globalization;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Registry;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Discovery;

/// <summary>What discovery found: the builds to start and notes about what it skipped.</summary>
internal sealed record DiscoveryResult(IReadOnlyList<BuildJob> Jobs, IReadOnlyList<string> Notes);

/// <summary>
/// Finds releases the hub has not seen: not drafts, tagged with the registration's prefix and a valid version the plugin never had, and
/// marked as a pre-release exactly when the version has a pre-release part. Each one gets a tracking issue and a build job.
/// </summary>
internal sealed class ReleaseDiscovery(IGitHubReader github, IGitHubWriter bot)
{
    public async Task<DiscoveryResult> DiscoverAsync(RegistryState registry, string? onlyPlugin, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var jobs = new List<BuildJob>();
        var tracked = (await github.GetIssuesAsync(HubLocations.Registry, HubLabels.Tracking, IssueFilter.All, cancellationToken).ConfigureAwait(false))
            .Select(issue => issue.Title).ToHashSet(StringComparer.Ordinal);

        var registrations = registry.Registrations.Where(registration => onlyPlugin is null || string.Equals(registration.Id, onlyPlugin, StringComparison.Ordinal)).ToList();
        if (onlyPlugin is not null && registrations.Count == 0)
            throw new ToolException($"{onlyPlugin} is not registered.");

        foreach (var registration in registrations.OrderBy(registration => registration.Id, StringComparer.Ordinal))
        {
            var publisher = registry.FindPublisher(PluginId.Parse(registration.Id).Publisher);
            if (Skip(registration, publisher) is { } reason)
            {
                if (onlyPlugin is not null)
                    notes.Add($"{registration.Id}: {reason}");
                continue;
            }

            var repository = await github.GetRepositoryAsync(registration.Repository.Id, cancellationToken).ConfigureAwait(false);
            if (repository is null || repository.Private || repository.Owner.Id != publisher!.Owner.Id)
            {
                notes.Add($"{registration.Id}: the repository is missing, private or owned by someone else; the nightly audit handles it.");
                continue;
            }

            var known = registry.VersionsOf(registration.Id).Select(record => record.Version).ToHashSet();
            var releases = await github.GetReleasesAsync(repository.FullName, cancellationToken).ConfigureAwait(false);
            foreach (var release in releases.Reverse())
            {
                if (Candidate(registration, release, known, tracked, notes) is not { } version)
                    continue;
                var commit = await github.ResolveTagAsync(repository.FullName, release.TagName, cancellationToken).ConfigureAwait(false);
                if (commit is null || release.Author is null)
                {
                    notes.Add($"{registration.Id}: the tag {release.TagName} or its release author could not be read.");
                    continue;
                }

                var title = TrackingIssues.Title(registration.Id, version);
                var issue = await bot.CreateIssueAsync(HubLocations.Registry, title,
                    TrackingIssues.Body(registration.Id, version, repository.FullName, release.TagName, registration.Maintainers), [HubLabels.Tracking], cancellationToken).ConfigureAwait(false);
                tracked.Add(title);
                known.Add(version);
                jobs.Add(new BuildJob
                {
                    Plugin = registration.Id,
                    Version = version.ToString(),
                    Tag = release.TagName,
                    Commit = commit,
                    Repository = repository.FullName,
                    RepositoryId = repository.Id,
                    Path = registration.Repository.Path,
                    ReleaseAuthor = release.Author,
                    TrackingIssue = issue,
                    ReleaseNotes = release.Body.Length > VersionRecordFactory.MaxReleaseNotes ? release.Body[..VersionRecordFactory.MaxReleaseNotes] : release.Body,
                });
                notes.Add(string.Create(CultureInfo.InvariantCulture, $"{registration.Id}: found {release.TagName} at {commit[..12]}; tracking issue #{issue}."));
            }
        }

        return new DiscoveryResult(jobs, notes);
    }

    private static string? Skip(Registration registration, PublisherFile? publisher) => (registration.State, publisher?.State) switch
    {
        (_, null) => "its publisher is not registered.",
        (RegistrationState.Frozen or RegistrationState.Blocked or RegistrationState.Deleted, _) => $"the plugin is {registration.State.ToString().ToLowerInvariant()}.",
        (_, not PublisherState.Active) => "its publisher is not active.",
        _ => null,
    };

    private static SemanticVersion? Candidate(Registration registration, GitHubRelease release, HashSet<SemanticVersion> known, HashSet<string> tracked, List<string> notes)
    {
        var prefix = registration.Repository.TagPrefix;
        if (release.Draft || !release.TagName.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        if (!SemanticVersion.TryParse(release.TagName[prefix.Length..], out var version))
        {
            notes.Add($"{registration.Id}: {release.TagName} is not a valid version tag.");
            return null;
        }

        if (known.Contains(version) || tracked.Contains(TrackingIssues.Title(registration.Id, version)))
            return null;
        if (version.IsPreRelease != release.Prerelease)
        {
            notes.Add($"{registration.Id}: {release.TagName} is {(release.Prerelease ? "" : "not ")}marked as a pre-release on GitHub, but its version says otherwise.");
            return null;
        }

        return version;
    }
}
