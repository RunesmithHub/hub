using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Resolution;

/// <summary>A plugin's known versions, lowest first, so a version's position is its index in version sets.</summary>
internal sealed class PackageVersions
{
    public PackageVersions(string id, PluginRecord? record, SemanticVersion? installed)
    {
        Id = id;
        Record = record;
        Versions = record is null ? [] : [.. record.Versions.OrderBy(version => version.Version)];
        var index = installed is null ? -1 : Versions.ToList().FindIndex(version => version.Version == installed);
        InstalledIndex = index < 0 ? null : index;
    }

    public string Id { get; }

    public PluginRecord? Record { get; }

    public IReadOnlyList<VersionRecord> Versions { get; }

    /// <summary>Gets the index of the installed version, when it is installed and in the index.</summary>
    public int? InstalledIndex { get; }
}
