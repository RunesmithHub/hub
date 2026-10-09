using System.Text;
using System.Text.Json;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Tool.Registry;

/// <summary>Reads the registry's records strictly, naming the file in every error.</summary>
internal sealed class RegistryReader(IRegistryFiles files)
{
    public IRegistryFiles Files { get; } = files;

    /// <summary>Reads everything the index and the pipeline need.</summary>
    public async Task<RegistryState> LoadAsync(CancellationToken cancellationToken)
    {
        var registrations = await RegistrationsAsync(cancellationToken).ConfigureAwait(false);
        var versions = new Dictionary<string, IReadOnlyList<RegistryVersionRecord>>(StringComparer.Ordinal);
        foreach (var registration in registrations)
            versions[registration.Id] = await VersionsAsync(registration.Id, cancellationToken).ConfigureAwait(false);

        return new RegistryState(
            await PublishersAsync(cancellationToken).ConfigureAwait(false),
            registrations,
            versions,
            await ModerationAsync(cancellationToken).ConfigureAwait(false),
            await PolicyAsync(cancellationToken).ConfigureAwait(false),
            await CategoriesAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<PublisherFile>> PublishersAsync(CancellationToken cancellationToken)
    {
        var result = new List<PublisherFile>();
        foreach (var path in await Files.ListAsync(RegistryPaths.Publishers, cancellationToken).ConfigureAwait(false))
        {
            if (RegistryPaths.IsPublisher(path, out _))
                result.Add(await ReadAsync<PublisherFile>(path, cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    public Task<PublisherFile?> PublisherAsync(string name, CancellationToken cancellationToken) =>
        ReadOptionalAsync<PublisherFile>(RegistryPaths.Publisher(name), cancellationToken);

    public async Task<IReadOnlyList<Registration>> RegistrationsAsync(CancellationToken cancellationToken)
    {
        var result = new List<Registration>();
        foreach (var path in await Files.ListAsync(RegistryPaths.Plugins, cancellationToken).ConfigureAwait(false))
        {
            if (RegistryPaths.IsRegistration(path, out _))
                result.Add(await ReadAsync<Registration>(path, cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    public Task<Registration?> RegistrationAsync(string pluginId, CancellationToken cancellationToken) =>
        ReadOptionalAsync<Registration>(RegistryPaths.Registration(pluginId), cancellationToken);

    /// <summary>Reads a plugin's version records, highest version first.</summary>
    public async Task<IReadOnlyList<RegistryVersionRecord>> VersionsAsync(string pluginId, CancellationToken cancellationToken)
    {
        var result = new List<RegistryVersionRecord>();
        foreach (var path in await Files.ListAsync(RegistryPaths.Versions(pluginId), cancellationToken).ConfigureAwait(false))
        {
            if (!RegistryPaths.IsVersion(path, out _, out var version))
                continue;
            var record = await ReadAsync<RegistryVersionRecord>(path, cancellationToken).ConfigureAwait(false);
            if (record.Version != version)
                throw new ToolException($"{path}: the record is for version {record.Version}.");
            result.Add(record);
        }

        return [.. result.OrderByDescending(record => record.Version)];
    }

    public Task<RegistryVersionRecord?> VersionAsync(string pluginId, SemanticVersion version, CancellationToken cancellationToken) =>
        ReadOptionalAsync<RegistryVersionRecord>(RegistryPaths.Version(pluginId, version), cancellationToken);

    /// <summary>Reads every moderation file, lowest ID first.</summary>
    public async Task<IReadOnlyList<RegistryModerationEntry>> ModerationAsync(CancellationToken cancellationToken)
    {
        var result = new List<RegistryModerationEntry>();
        foreach (var path in await Files.ListAsync(RegistryPaths.Moderation, cancellationToken).ConfigureAwait(false))
        {
            if (!RegistryPaths.IsModerationEntry(path))
                continue;
            var entry = await ReadAsync<RegistryModerationEntry>(path, cancellationToken).ConfigureAwait(false);
            if (!path.EndsWith(RegistryModerationEntry.FileName(entry.Id), StringComparison.Ordinal))
                throw new ToolException($"{path}: the entry has ID {entry.Id}.");
            result.Add(entry);
        }

        return [.. result.OrderBy(entry => entry.Id)];
    }

    public async Task<RegistryPolicy> PolicyAsync(CancellationToken cancellationToken) =>
        await ReadOptionalAsync<RegistryPolicy>(RegistryPaths.Policy, cancellationToken).ConfigureAwait(false) ?? new RegistryPolicy();

    public async Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken cancellationToken) =>
        await ReadOptionalAsync<IReadOnlyList<string>>(RegistryPaths.Categories, cancellationToken).ConfigureAwait(false) ?? Protocol.Categories.Default;

    public async Task<IReadOnlySet<string>> ReservedNamesAsync(CancellationToken cancellationToken)
    {
        var bytes = await Files.ReadAsync(RegistryPaths.ReservedNames, cancellationToken).ConfigureAwait(false);
        return bytes is null ? new HashSet<string>(StringComparer.Ordinal) : ReservedNames.Parse(Encoding.UTF8.GetString(bytes));
    }

    /// <summary>Reads the signed roots <c>keys/root/1.json</c> upward, stopping at the first gap.</summary>
    public async Task<IReadOnlyList<byte[]>> RootsAsync(CancellationToken cancellationToken)
    {
        var roots = new List<byte[]>();
        for (var version = 1; ; version++)
        {
            var bytes = await Files.ReadAsync(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{RegistryPaths.Roots}/{version}.json"), cancellationToken).ConfigureAwait(false);
            if (bytes is null)
                return roots;
            roots.Add(bytes);
        }
    }

    private async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken)
        where T : class =>
        await ReadOptionalAsync<T>(path, cancellationToken).ConfigureAwait(false) ?? throw new ToolException($"{path} does not exist.");

    private async Task<T?> ReadOptionalAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        var bytes = await Files.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : Parse<T>(bytes, path);
    }

    /// <summary>Parses a registry file strictly.</summary>
    /// <exception cref="ToolException">The file is not valid; the message names it.</exception>
    public static T Parse<T>(byte[] bytes, string path)
        where T : class
    {
        try
        {
            return RegistryJson.Deserialize<T>(bytes);
        }
        catch (JsonException exception)
        {
            throw new ToolException($"{path}: {exception.Message}", exception);
        }
    }
}
