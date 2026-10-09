using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Updating;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Catalog;

/// <summary>Queries over a verified index view: plugins, publishers, effective states and installable versions.</summary>
public sealed class HubCatalog
{
    private readonly IReadOnlyDictionary<string, PluginRecord> _plugins;
    private readonly PublisherList _publishers;
    private readonly IReadOnlyList<ModerationEntry> _moderation;

    public HubCatalog(IndexView view)
        : this(view?.Plugins.Values ?? throw new ArgumentNullException(nameof(view)), view.Publishers, view.Moderation.Entries)
    {
    }

    /// <summary>Creates a catalog from records directly, such as sample data.</summary>
    public HubCatalog(IEnumerable<PluginRecord> plugins, PublisherList publishers, IReadOnlyList<ModerationEntry> moderation)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(publishers);
        ArgumentNullException.ThrowIfNull(moderation);
        _plugins = plugins.ToDictionary(plugin => plugin.Id, StringComparer.Ordinal);
        _publishers = publishers;
        _moderation = moderation;
    }

    public IEnumerable<PluginRecord> Plugins => _plugins.Values;

    public PluginRecord? FindPlugin(string id) => _plugins.GetValueOrDefault(id);

    public PublisherEntry? FindPublisher(string name) => _publishers.Find(name);

    public EffectiveState GetEffectiveState(PluginRecord plugin, VersionRecord version)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        return EffectiveState.Of(version, plugin, FindPublisher(plugin.Publisher), _moderation);
    }

    /// <summary>Gets a version's effective state, or null when the index does not have the version.</summary>
    public EffectiveState? GetEffectiveState(string pluginId, SemanticVersion version) =>
        FindPlugin(pluginId) is { } plugin && plugin.FindVersion(version) is { } record ? GetEffectiveState(plugin, record) : null;

    /// <summary>
    /// Lists the versions a user may install, highest first: effectively published, running on the host, of an allowed tier, and
    /// pre-releases only when <paramref name="allowPreReleases"/> is set.
    /// </summary>
    public IReadOnlyList<VersionRecord> GetInstallableVersions(string pluginId, RunesmithHost host, AllowedTiers tiers = AllowedTiers.All, bool allowPreReleases = false)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (FindPlugin(pluginId) is not { } plugin || !tiers.Allows(plugin.Tier))
            return [];

        return [.. plugin.Versions
            .Where(version => (allowPreReleases || !version.Version.IsPreRelease) && host.CanRun(version) && GetEffectiveState(plugin, version).IsInstallable)
            .OrderByDescending(version => version.Version)];
    }
}
