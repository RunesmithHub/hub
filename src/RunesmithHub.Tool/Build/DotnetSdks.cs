using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Tool.Build;

/// <summary>The .NET SDK the hub builds with for each line of the Runesmith API, whatever SDK the plugin's repository pins.</summary>
internal static class DotnetSdks
{
    private static readonly Dictionary<string, string> ByApiLine = new(StringComparer.Ordinal)
    {
        ["0.1"] = "10.0.401",
        ["0.2"] = "10.0.401",
    };

    /// <summary>Gets the SDK version for the line of a plugin's minimum Runesmith API version.</summary>
    /// <exception cref="ToolException">The hub does not build for that line.</exception>
    public static string For(SemanticVersion apiMinimum)
    {
        ArgumentNullException.ThrowIfNull(apiMinimum);
        return ByApiLine.TryGetValue(apiMinimum.Line.ToString(), out var sdk)
            ? sdk
            : throw new ToolException($"The hub does not build plugins for Runesmith API {apiMinimum.Line}; supported: {string.Join(", ", ByApiLine.Keys)}.");
    }
}
