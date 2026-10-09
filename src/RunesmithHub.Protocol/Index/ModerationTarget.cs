using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Index;

/// <summary>What a moderation entry applies to.</summary>
public sealed record ModerationTarget
{
    public required ModerationTargetKind Kind { get; init; }

    /// <summary>Gets the publisher name, for a publisher target.</summary>
    public string? Publisher { get; init; }

    /// <summary>Gets the plugin ID, for a plugin or versions target.</summary>
    public string? Plugin { get; init; }

    /// <summary>Gets the versions, for a versions target.</summary>
    public VersionRange? Range { get; init; }

    /// <summary>Gets whether the target covers a plugin version; ranges include pre-releases.</summary>
    public bool Covers(string publisher, string pluginId, SemanticVersion version) => Kind switch
    {
        ModerationTargetKind.Publisher => string.Equals(Publisher, publisher, StringComparison.Ordinal),
        ModerationTargetKind.Plugin => string.Equals(Plugin, pluginId, StringComparison.Ordinal),
        ModerationTargetKind.Versions => string.Equals(Plugin, pluginId, StringComparison.Ordinal) && Range is not null
            && Range.Contains(version, allowPreRelease: true),
        _ => false,
    };

    /// <summary>Returns the target as administrators write it: <c>publisher:fastcode</c>, <c>lumen.todo</c> or <c>lumen.todo@1.3.1</c>.</summary>
    public override string ToString() => Kind switch
    {
        ModerationTargetKind.Publisher => $"publisher:{Publisher}",
        ModerationTargetKind.Versions => $"{Plugin}@{Range}",
        _ => Plugin ?? "",
    };
}
