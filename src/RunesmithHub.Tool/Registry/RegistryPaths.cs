using System.Diagnostics.CodeAnalysis;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Tool.Registry;

/// <summary>Where each kind of record lives in the registry repository.</summary>
internal static class RegistryPaths
{
    public const string Publishers = "publishers";
    public const string Plugins = "plugins";
    public const string Moderation = "moderation";
    public const string Policy = "policy/policy.json";
    public const string Categories = "policy/categories.json";
    public const string ReservedNames = "policy/reserved-names.txt";
    public const string Roots = "keys/root";

    public static string Publisher(string name) => $"{Publishers}/{name}.json";

    public static string Registration(string pluginId) => $"{Plugins}/{pluginId}/registration.json";

    public static string Versions(string pluginId) => $"{Plugins}/{pluginId}/versions";

    public static string Version(string pluginId, SemanticVersion version) => $"{Versions(pluginId)}/{version}.json";

    public static string ModerationEntry(int id) => $"{Moderation}/{RegistryModerationEntry.FileName(id)}";

    /// <summary>Recognises <c>plugins/&lt;id&gt;/versions/&lt;version&gt;.json</c>.</summary>
    public static bool IsVersion(string path, [NotNullWhen(true)] out string? pluginId, [NotNullWhen(true)] out SemanticVersion? version)
    {
        pluginId = null;
        version = null;
        var parts = path.Split('/');
        if (parts is not [Plugins, var id, "versions", var file] || !file.EndsWith(".json", StringComparison.Ordinal))
            return false;
        if (!SemanticVersion.TryParse(file[..^".json".Length], out version))
            return false;
        pluginId = id;
        return true;
    }

    /// <summary>Recognises <c>plugins/&lt;id&gt;/registration.json</c>.</summary>
    public static bool IsRegistration(string path, [NotNullWhen(true)] out string? pluginId)
    {
        var parts = path.Split('/');
        pluginId = parts is [Plugins, var id, "registration.json"] ? id : null;
        return pluginId is not null;
    }

    /// <summary>Recognises <c>publishers/&lt;name&gt;.json</c>.</summary>
    public static bool IsPublisher(string path, [NotNullWhen(true)] out string? name)
    {
        var parts = path.Split('/');
        name = parts is [Publishers, var file] && file.EndsWith(".json", StringComparison.Ordinal) ? file[..^".json".Length] : null;
        return name is not null;
    }

    /// <summary>Recognises <c>moderation/&lt;NNNNN&gt;.json</c>.</summary>
    public static bool IsModerationEntry(string path) =>
        path.Split('/') is [Moderation, var file] && file.Length == 10 && file.EndsWith(".json", StringComparison.Ordinal) && file[..5].All(char.IsAsciiDigit);
}
