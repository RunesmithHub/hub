using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Protocol.Registry;

/// <summary>A moderation file, <c>moderation/&lt;NNNNN&gt;.json</c>: the entry, who created it, and whether it was lifted.</summary>
public sealed record RegistryModerationEntry : ModerationEntry
{
    /// <summary>Gets the GitHub user ID of the administrator who created it.</summary>
    [JsonPropertyOrder(100)]
    public required long CreatedBy { get; init; }

    [JsonPropertyOrder(101)]
    public DateTimeOffset? LiftedAt { get; init; }

    [JsonPropertyOrder(102)]
    public long? LiftedBy { get; init; }

    /// <summary>Gets whether the entry still applies.</summary>
    [JsonIgnore]
    public bool IsActive => LiftedAt is null;

    /// <summary>Gets the registry file name for an entry ID, such as <c>00007.json</c>.</summary>
    public static string FileName(int id) => id.ToString("D5", System.Globalization.CultureInfo.InvariantCulture) + ".json";

    /// <summary>Returns the entry as the moderation list carries it.</summary>
    public ModerationEntry ToIndexEntry() => new()
    {
        Id = Id,
        Target = Target,
        Action = Action,
        Reason = Reason,
        Note = Note,
        Advisory = Advisory,
        Remediation = Remediation,
        CreatedAt = CreatedAt,
    };
}
