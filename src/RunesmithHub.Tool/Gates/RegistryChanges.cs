using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Gates;

/// <summary>A version record a change adds, edits or removes.</summary>
internal sealed record VersionChange(string Path, string PluginId, SemanticVersion Version, RegistryVersionRecord? Before, RegistryVersionRecord? After);

/// <summary>
/// What a pull request changes in the registry, read from Git: the version records, and the changes that loosen a restriction and so
/// follow the strict path.
/// </summary>
internal sealed record RegistryChanges(IReadOnlyList<ChangedPath> Paths, IReadOnlyList<VersionChange> Versions, IReadOnlyList<StrictChange> Strict)
{
    /// <summary>Reads the changes between two commits.</summary>
    /// <exception cref="ToolException">A changed record is not valid.</exception>
    public static async Task<RegistryChanges> ReadAsync(IGitRepository git, string baseRef, string headRef, CancellationToken cancellationToken)
    {
        var paths = await git.DiffAsync(baseRef, headRef, cancellationToken).ConfigureAwait(false);
        var before = new GitRegistryFiles(git, baseRef);
        var after = new GitRegistryFiles(git, headRef);
        var versions = new List<VersionChange>();
        var strict = new List<StrictChange>();
        foreach (var change in paths)
        {
            if (RegistryPaths.IsVersion(change.Path, out var pluginId, out var version))
            {
                versions.Add(new VersionChange(change.Path, pluginId, version,
                    await ReadAsync<RegistryVersionRecord>(before, change.Path, cancellationToken).ConfigureAwait(false),
                    await ReadAsync<RegistryVersionRecord>(after, change.Path, cancellationToken).ConfigureAwait(false)));
            }

            strict.AddRange(await StrictPath.DetectAsync(change.Path, before, after, cancellationToken).ConfigureAwait(false));
        }

        return new RegistryChanges(paths, versions, strict);
    }

    private static async Task<T?> ReadAsync<T>(GitRegistryFiles files, string path, CancellationToken cancellationToken)
        where T : class =>
        await files.ReadAsync(path, cancellationToken).ConfigureAwait(false) is { } bytes ? RegistryReader.Parse<T>(bytes, path) : null;
}

/// <summary>A change that makes the hub less strict, with the administrator who asked for it when the record says.</summary>
internal sealed record StrictChange(string Path, string Description, long? RequestedBy);

/// <summary>Recognises strict-path changes: lifting a block or a security freeze, verifying, raising a tier, lifting a suspension, and unblocking or unfreezing a record.</summary>
internal static class StrictPath
{
    private static readonly ModerationReason[] SecurityReasons = [ModerationReason.Malicious, ModerationReason.CompromisedAccount, ModerationReason.Vulnerability];

    public static bool IsSecurityRelated(ModerationReason reason) => SecurityReasons.Contains(reason);

    public static async Task<IReadOnlyList<StrictChange>> DetectAsync(string path, IRegistryFiles before, IRegistryFiles after, CancellationToken cancellationToken)
    {
        var old = await before.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        var now = await after.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        if (old is null || now is null)
            return [];

        if (RegistryPaths.IsModerationEntry(path))
        {
            var (was, @is) = (RegistryReader.Parse<RegistryModerationEntry>(old, path), RegistryReader.Parse<RegistryModerationEntry>(now, path));
            return was.IsActive && !@is.IsActive && (was.Action == ModerationAction.Block || IsSecurityRelated(was.Reason))
                ? [new StrictChange(path, $"lifts {was.Action.ToString().ToLowerInvariant()} {was.Id} ({was.Target})", @is.LiftedBy)]
                : [];
        }

        if (RegistryPaths.IsRegistration(path, out _))
        {
            var (was, @is) = (RegistryReader.Parse<Registration>(old, path), RegistryReader.Parse<Registration>(now, path));
            var changes = new List<StrictChange>();
            if (Rank(@is.Tier) > Rank(was.Tier))
                changes.Add(new StrictChange(path, $"raises {was.Id} to {@is.Tier.ToString().ToLowerInvariant()}", null));
            if (was.State is RegistrationState.Frozen or RegistrationState.Blocked && @is.State is not (RegistrationState.Frozen or RegistrationState.Blocked or RegistrationState.Deleted))
                changes.Add(new StrictChange(path, $"lifts the {was.State.ToString().ToLowerInvariant()} state of {was.Id}", null));
            return changes;
        }

        if (RegistryPaths.IsPublisher(path, out _))
        {
            var (was, @is) = (RegistryReader.Parse<PublisherFile>(old, path), RegistryReader.Parse<PublisherFile>(now, path));
            var changes = new List<StrictChange>();
            if (was.Verified is null && @is.Verified is not null)
                changes.Add(new StrictChange(path, $"verifies the publisher {was.Name}", @is.Verified.By));
            if (was.State != PublisherState.Active && @is.State == PublisherState.Active)
                changes.Add(new StrictChange(path, $"makes the publisher {was.Name} active again", null));
            return changes;
        }

        if (RegistryPaths.IsVersion(path, out _, out _))
        {
            var (was, @is) = (RegistryReader.Parse<RegistryVersionRecord>(old, path), RegistryReader.Parse<RegistryVersionRecord>(now, path));
            return was.State is VersionState.Frozen or VersionState.Blocked && @is.State is not (VersionState.Frozen or VersionState.Blocked or VersionState.Deleted)
                ? [new StrictChange(path, $"lifts the {was.State.ToString().ToLowerInvariant()} state of version {was.Version}", null)]
                : [];
        }

        return [];
    }

    private static int Rank(PluginTier tier) => tier switch
    {
        PluginTier.Official => 2,
        PluginTier.Verified => 1,
        _ => 0,
    };
}
