namespace RunesmithHub.Protocol;

/// <summary>The assemblies Runesmith loads once and shares with every plugin, which a package must never bundle.</summary>
public static class HostAssemblies
{
    private static readonly string[] ExactNames =
    [
        "Runesmith.Sdk", "Runesmith.Text", "Runesmith.LanguageServices", "HammerUI", "Microsoft.Extensions.Logging.Abstractions", "SkiaSharp", "HarfBuzzSharp",
        "Tmds.DBus.Protocol", "MicroCom.Runtime",
    ];

    private static readonly string[] Prefixes = ["Avalonia", "Microsoft.VisualStudio.Composition", "System.Composition", "MessagePack"];

    /// <summary>Gets the names that must match exactly.</summary>
    public static IReadOnlyList<string> Names => ExactNames;

    /// <summary>Gets the prefixes of whole assembly families, such as every <c>Avalonia*</c> assembly.</summary>
    public static IReadOnlyList<string> FamilyPrefixes => Prefixes;

    /// <summary>Gets whether an assembly name, without its <c>.dll</c> extension, is host-shared. Names compare case-insensitively.</summary>
    public static bool IsHostShared(string assemblyName) =>
        ExactNames.Contains(assemblyName, StringComparer.OrdinalIgnoreCase)
        || Prefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
