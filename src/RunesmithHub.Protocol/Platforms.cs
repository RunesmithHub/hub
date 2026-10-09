namespace RunesmithHub.Protocol;

/// <summary>The platform names a manifest's <c>platforms</c> may list.</summary>
public static class Platforms
{
    public const string Windows = "windows";
    public const string Linux = "linux";
    public const string MacOS = "macos";

    public static IReadOnlyList<string> All { get; } = [Windows, Linux, MacOS];

    /// <summary>Gets the platform this process runs on, or null on any other operating system.</summary>
    public static string? Current =>
        OperatingSystem.IsWindows() ? Windows : OperatingSystem.IsLinux() ? Linux : OperatingSystem.IsMacOS() ? MacOS : null;

    public static bool IsKnown(string name) => All.Contains(name, StringComparer.Ordinal);
}
