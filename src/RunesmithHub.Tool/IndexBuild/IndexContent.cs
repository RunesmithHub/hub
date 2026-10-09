using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.IndexBuild;

/// <summary>
/// Builds the index's documents from the registry. Publication facts (the published state, when, and who reviewed) are never in the
/// registry: they come from this run's publications or are carried forward from the previous signed index.
/// </summary>
internal static class IndexContent
{
    /// <summary>Builds the plugin records, without versions numbers, for every plugin that has a version record.</summary>
    /// <param name="previous">Gets the previous index's record of a plugin, if any.</param>
    public static IReadOnlyList<PluginRecord> PluginRecords(RegistryState registry, IReadOnlyDictionary<string, Publication> publications, Func<string, PluginRecord?> previous)
    {
        var records = new List<PluginRecord>();
        foreach (var registration in registry.Registrations.OrderBy(registration => registration.Id, StringComparer.Ordinal))
        {
            var versions = registry.VersionsOf(registration.Id);
            if (versions.Count == 0)
                continue;
            var before = previous(registration.Id);
            var indexVersions = versions.Select(version => Version(registration.Id, version, publications, before?.FindVersion(version.Version))).ToList();
            records.Add(Plugin(registration, versions, indexVersions));
        }

        return records;
    }

    public static PublisherList Publishers(RegistryState registry) => new()
    {
        Type = PayloadTypes.Publishers,
        Version = 1,
        Publishers = [.. registry.Publishers.OrderBy(publisher => publisher.Name, StringComparer.Ordinal).Select(publisher => new PublisherEntry
        {
            Name = publisher.Name,
            DisplayName = publisher.DisplayName,
            Owner = publisher.Owner,
            State = publisher.State,
            Verified = publisher.Verified is not null,
            VerifiedAt = publisher.Verified?.At,
        })],
    };

    public static PolicyPayload Policy(RegistryState registry) => new()
    {
        Type = PayloadTypes.Policy,
        Version = 1,
        Categories = registry.Categories,
        Capabilities = Capabilities.DefaultLabels.ToDictionary(pair => pair.Key, pair => new CapabilityLabel { Label = pair.Value }, StringComparer.Ordinal),
        OldestRunesmith = registry.Policy.OldestRunesmith,
        Mirrors = registry.Policy.Mirrors,
        HoldHours = registry.Policy.HoldHours,
    };

    /// <summary>Gets the active moderation entries as the moderation list carries them, lowest ID first.</summary>
    public static IReadOnlyList<ModerationEntry> ModerationEntries(RegistryState registry) =>
        [.. registry.ActiveModeration.OrderBy(entry => entry.Id).Select(entry => entry.ToIndexEntry())];

    private static VersionRecord Version(string pluginId, RegistryVersionRecord record, IReadOnlyDictionary<string, Publication> publications, VersionRecord? before)
    {
        var publication = publications.GetValueOrDefault($"{pluginId}@{record.Version}");
        var publishedAt = publication?.PublishedAt ?? before?.PublishedAt;
        var wasPublished = publishedAt is not null;
        var state = record.State switch
        {
            VersionState.Review or VersionState.Held or VersionState.Published when wasPublished => VersionState.Published,
            VersionState.Published => VersionState.Review,
            var other => other,
        };
        var hosted = wasPublished && state is VersionState.Published or VersionState.Yanked or VersionState.Frozen;
        return record.ToIndexRecord() with
        {
            State = state,
            Review = publication?.Review ?? before?.Review ?? record.Review,
            Package = hosted ? record.Package : null,
            Source = hosted ? record.Source : null,
            Sbom = hosted ? record.Sbom : null,
            Analysis = record.Analysis with { Triggers = [] },
            PublishedAt = wasPublished ? publishedAt : null,
            HoldUntil = state == VersionState.Held ? record.HoldUntil : null,
            YankReason = state == VersionState.Yanked ? record.YankReason : null,
        };
    }

    private static PluginRecord Plugin(Registration registration, IReadOnlyList<RegistryVersionRecord> registryVersions, List<VersionRecord> versions)
    {
        var listing = Listing(versions);
        var assets = registryVersions.First(version => version.Version == listing.Version).Assets
            ?? throw new ToolException($"{registration.Id} {listing.Version} has no assets, so the plugin has no icon.");
        var id = PluginId.Parse(registration.Id);
        return new PluginRecord
        {
            Type = PayloadTypes.Plugin,
            Version = 1,
            Id = registration.Id,
            Name = listing.Name,
            Publisher = id.Publisher,
            Tier = registration.Tier,
            State = registration.State switch
            {
                RegistrationState.Deprecated => PluginState.Deprecated,
                RegistrationState.Frozen => PluginState.Frozen,
                RegistrationState.Blocked => PluginState.Blocked,
                RegistrationState.Deleted => PluginState.Deleted,
                _ => PluginState.Listed,
            },
            Repository = new RecordRepository { Id = registration.Repository.Id, Url = registration.Repository.Url, Path = registration.Repository.Path },
            Maintainers = registration.Maintainers,
            Summary = listing.Summary,
            Categories = listing.Categories,
            Keywords = listing.Keywords,
            Deprecated = registration.Deprecated ?? listing.Deprecated,
            Icon = assets.Icon,
            Banner = assets.Banner,
            Gallery = assets.Gallery,
            Versions = [.. versions.OrderByDescending(version => version.Version)],
        };
    }

    /// <summary>Picks the version whose manifest lists the plugin: the newest published stable one, else the newest published, else the newest.</summary>
    private static VersionRecord Listing(List<VersionRecord> versions)
    {
        var published = versions.Where(version => version.PublishedAt is not null).ToList();
        return published.Where(version => !version.Version.IsPreRelease).MaxBy(version => version.Version)
            ?? published.MaxBy(version => version.Version)
            ?? versions.MaxBy(version => version.Version)!;
    }
}
