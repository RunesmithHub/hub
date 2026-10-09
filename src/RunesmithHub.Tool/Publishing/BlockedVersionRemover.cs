using System.Globalization;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Publishing;

/// <summary>A blocked version, and whether it was blocked for legal reasons.</summary>
internal sealed record BlockedVersion(string PluginId, RegistryVersionRecord Record, bool Legal);

/// <summary>
/// Removes what the hub hosts for blocked versions: their release files in the packages repository, keeping the source archive for
/// investigation unless the block is for legal reasons. The index's state delists their contracts packages; only a legal block also
/// deletes the contracts package from the hub feed.
/// </summary>
internal sealed class BlockedVersionRemover(IGitHubReader github, IGitHubWriter bot, IFeedPublisher feed)
{
    public async Task<IReadOnlyList<string>> RemoveAsync(RegistryState registry, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        foreach (var blocked in Blocked(registry))
        {
            var version = blocked.Record.Version;
            var removed = 0;
            if (await github.GetReleaseByTagAsync(HubLocations.Packages, HubLocations.ReleaseTag(blocked.PluginId, version), cancellationToken).ConfigureAwait(false) is { } release)
            {
                var kept = blocked.Legal ? null : BuildArtifacts.Source(blocked.PluginId, version.ToString());
                foreach (var asset in release.Assets.Where(asset => !string.Equals(asset.Name, kept, StringComparison.Ordinal)))
                {
                    await bot.DeleteAssetAsync(HubLocations.Packages, asset.Id, cancellationToken).ConfigureAwait(false);
                    removed++;
                }
            }

            var delisted = blocked.Legal && await feed.DeleteAsync(blocked.Record.Contracts.Package, blocked.Record.Contracts.PackageVersion, cancellationToken).ConfigureAwait(false);
            if (removed > 0 || delisted)
            {
                notes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{blocked.PluginId} {version}: removed {removed} release files{(delisted ? " and the contracts package" : "")}."));
            }
        }

        if (notes.Count == 0)
            notes.Add("Nothing to remove.");
        return notes;
    }

    /// <summary>Lists every version that its record, its plugin, its publisher or an active moderation entry blocks.</summary>
    public static IEnumerable<BlockedVersion> Blocked(RegistryState registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (var registration in registry.Registrations.OrderBy(registration => registration.Id, StringComparer.Ordinal))
        {
            var publisher = PluginId.Parse(registration.Id).Publisher;
            var publisherBlocked = registry.FindPublisher(publisher)?.State == PublisherState.Blocked;
            foreach (var record in registry.VersionsOf(registration.Id))
            {
                var blocks = registry.ActiveModeration
                    .Where(entry => entry.Action == ModerationAction.Block && entry.Target.Covers(publisher, registration.Id, record.Version))
                    .ToList();
                if (blocks.Count > 0 || publisherBlocked || registration.State == RegistrationState.Blocked || record.State == VersionState.Blocked)
                    yield return new BlockedVersion(registration.Id, record, blocks.Any(entry => entry.Reason == ModerationReason.Legal));
            }
        }
    }
}
