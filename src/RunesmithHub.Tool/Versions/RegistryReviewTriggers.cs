using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Versions;

/// <summary>The review triggers only the registry knows: the release author, the maintainers, and what happened to the previous version.</summary>
internal static class RegistryReviewTriggers
{
    /// <summary>Lists the registry's triggers for a new version.</summary>
    /// <param name="previous">The highest earlier version record, if any.</param>
    /// <param name="previousMaintainers">The maintainers when <paramref name="previous"/> was added, when known.</param>
    public static IEnumerable<string> For(
        Registration registration, PublisherFile publisher, GitHubAccount releaseAuthor, RegistryVersionRecord? previous, IReadOnlySet<long>? previousMaintainers,
        RegistryState registry)
    {
        if (!registration.Maintainers.Any(maintainer => maintainer.Id == releaseAuthor.Id))
            yield return ReviewTriggers.ReleaseAuthor;
        if (previous is null)
            yield break;

        if (previousMaintainers is not null && !previousMaintainers.SetEquals(registration.Maintainers.Select(maintainer => maintainer.Id)))
            yield return ReviewTriggers.MaintainersChanged;
        if (WasRestricted(previous, registration, publisher, registry))
            yield return ReviewTriggers.PreviousRestricted;
    }

    /// <summary>Puts trigger codes in the canonical order without duplicates.</summary>
    public static IReadOnlyList<string> Canonical(IEnumerable<string> triggers)
    {
        var set = triggers.ToHashSet(StringComparer.Ordinal);
        return [.. ReviewTriggers.All.Where(set.Contains)];
    }

    private static bool WasRestricted(RegistryVersionRecord previous, Registration registration, PublisherFile publisher, RegistryState registry) =>
        previous.State is VersionState.Frozen or VersionState.Blocked
        || registry.ActiveModeration.Any(entry => entry.Target.Covers(publisher.Name, registration.Id, previous.Version));
}
