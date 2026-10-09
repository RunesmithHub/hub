namespace RunesmithHub.Tool.Analysis.Isolation;

/// <summary>Relates contracts assemblies to plugin IDs: <c>Lumen.Todo.Contracts</c> belongs to <c>lumen.todo</c>.</summary>
internal static class ContractsNames
{
    public const string Suffix = ".Contracts";

    /// <summary>Gets whether an assembly name is shaped like a plugin's contracts assembly.</summary>
    public static bool IsContracts(string assemblyName) => assemblyName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets whether a contracts assembly belongs to a plugin, comparing letters and digits only, ignoring case.</summary>
    public static bool BelongsTo(string assemblyName, string pluginId) =>
        IsContracts(assemblyName) && string.Equals(Letters(assemblyName[..^Suffix.Length]), Letters(pluginId), StringComparison.Ordinal);

    private static string Letters(string text) => new([.. text.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant)]);
}
