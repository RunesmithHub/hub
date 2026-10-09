namespace RunesmithHub.Protocol;

/// <summary>The capability IDs a plugin declares and the analysis detects.</summary>
public static class Capabilities
{
    public const string Network = "network";
    public const string Process = "process";
    public const string FileSystem = "filesystem";
    public const string Environment = "environment";
    public const string Credentials = "credentials";
    public const string Native = "native";
    public const string DynamicCode = "dynamic-code";

    /// <summary>Gets every capability ID, in the order they are shown.</summary>
    public static IReadOnlyList<string> All { get; } = [Network, Process, FileSystem, Environment, Credentials, Native, DynamicCode];

    /// <summary>Gets the default description users see for each capability.</summary>
    public static IReadOnlyDictionary<string, string> DefaultLabels { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Network] = "Connects to the internet or other computers",
        [Process] = "Starts other programs",
        [FileSystem] = "Reads or writes files outside your project and its own storage",
        [Environment] = "Reads environment variables, the registry or system settings",
        [Credentials] = "Stores or reads passwords and tokens",
        [Native] = "Runs native code",
        [DynamicCode] = "Loads or generates code while running",
    };

    public static bool IsKnown(string id) => All.Contains(id, StringComparer.Ordinal);
}
