using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Protocol.Registry;

namespace RunesmithHub.Tool.Registry;

/// <summary>Every record of the registry at one point.</summary>
/// <param name="Versions">Each plugin's version records, highest version first.</param>
internal sealed record RegistryState(
    IReadOnlyList<PublisherFile> Publishers,
    IReadOnlyList<Registration> Registrations,
    IReadOnlyDictionary<string, IReadOnlyList<RegistryVersionRecord>> Versions,
    IReadOnlyList<RegistryModerationEntry> Moderation,
    RegistryPolicy Policy,
    IReadOnlyList<string> Categories)
{
    public PublisherFile? FindPublisher(string name) => Publishers.FirstOrDefault(publisher => string.Equals(publisher.Name, name, StringComparison.Ordinal));

    public Registration? FindRegistration(string pluginId) => Registrations.FirstOrDefault(registration => string.Equals(registration.Id, pluginId, StringComparison.Ordinal));

    public IReadOnlyList<RegistryVersionRecord> VersionsOf(string pluginId) => Versions.GetValueOrDefault(pluginId) ?? [];

    public IEnumerable<RegistryModerationEntry> ActiveModeration => Moderation.Where(entry => entry.IsActive);

    /// <summary>Gets the package limits of a plugin: its registration's, else its publisher's, never below the defaults.</summary>
    public PackageLimits LimitsOf(Registration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return PackageLimits.From(registration.Limits ?? FindPublisher(PluginId.Parse(registration.Id).Publisher)?.Limits);
    }
}
