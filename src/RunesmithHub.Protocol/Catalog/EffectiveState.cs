using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Protocol.Catalog;

/// <summary>A version's state after combining its own, its plugin's, its publisher's and every moderation entry that targets it.</summary>
/// <param name="Moderation">The moderation entry behind the state, when one is; it carries the reason, note and advisory.</param>
public sealed record EffectiveState(VersionState State, ModerationEntry? Moderation = null)
{
    /// <summary>Gets whether the version can be installed or offered as an update.</summary>
    public bool IsInstallable => State == VersionState.Published;

    /// <summary>Gets how restrictive a state is: published, then review and held, yanked, deleted, frozen, and blocked as the most.</summary>
    public static int Restrictiveness(VersionState state) => state switch
    {
        VersionState.Published => 0,
        VersionState.Review or VersionState.Held => 1,
        VersionState.Yanked => 2,
        VersionState.Deleted => 3,
        VersionState.Frozen => 4,
        VersionState.Blocked => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    /// <summary>Combines the states that apply to a version, keeping the most restrictive.</summary>
    /// <param name="publisher">The plugin's publisher, or null when the publisher list does not have it.</param>
    /// <param name="moderation">The active moderation entries; only those that target the version count.</param>
    public static EffectiveState Of(VersionRecord version, PluginRecord plugin, PublisherEntry? publisher, IEnumerable<ModerationEntry> moderation)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(moderation);

        var state = Stricter(version.State, PluginStateAsVersionState(plugin.State));
        if (publisher?.State == PublisherState.Blocked)
            state = VersionState.Blocked;

        ModerationEntry? cause = null;
        foreach (var entry in moderation.Where(entry => entry.Target.Covers(plugin.Publisher, plugin.Id, version.Version)))
        {
            var entryState = entry.Action == ModerationAction.Block ? VersionState.Blocked : VersionState.Frozen;
            if (cause is null || Restrictiveness(entryState) > Restrictiveness(StateOf(cause)))
                cause = entry;
            state = Stricter(state, entryState);
        }

        return new EffectiveState(state, cause is not null && StateOf(cause) == state ? cause : null);
    }

    private static VersionState StateOf(ModerationEntry entry) => entry.Action == ModerationAction.Block ? VersionState.Blocked : VersionState.Frozen;

    private static VersionState Stricter(VersionState left, VersionState right) => Restrictiveness(right) > Restrictiveness(left) ? right : left;

    private static VersionState PluginStateAsVersionState(PluginState state) => state switch
    {
        PluginState.Frozen => VersionState.Frozen,
        PluginState.Blocked => VersionState.Blocked,
        PluginState.Deleted => VersionState.Deleted,
        _ => VersionState.Published,
    };
}
