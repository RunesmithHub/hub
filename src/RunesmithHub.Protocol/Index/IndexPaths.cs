using System.Globalization;

namespace RunesmithHub.Protocol.Index;

/// <summary>The relative paths of the index files, and the most bytes a client reads of each.</summary>
public static class IndexPaths
{
    public const string Timestamp = "timestamp.json";
    public const string Dashboard = "dashboard.json";
    public const string Stats = "stats.json";

    public const long RootLimit = 64 * 1024;
    public const long TimestampLimit = 64 * 1024;
    public const long SnapshotLimit = 8 * 1024 * 1024;
    public const long ModerationLimit = 8 * 1024 * 1024;
    public const long PluginRecordLimit = 1024 * 1024;
    public const long PublishersLimit = 4 * 1024 * 1024;
    public const long PolicyLimit = 4 * 1024 * 1024;

    public static string Root(int version) => string.Create(CultureInfo.InvariantCulture, $"root/{version}.json");

    public static string Snapshot(string sha256) => $"snapshot/{sha256}.json";

    public static string Moderation(string sha256) => $"moderation/{sha256}.json";

    public static string Plugin(string sha256) => $"plugins/{sha256}.json";

    public static string Publishers(string sha256) => $"publishers/{sha256}.json";

    public static string Policy(string sha256) => $"policy/{sha256}.json";
}
