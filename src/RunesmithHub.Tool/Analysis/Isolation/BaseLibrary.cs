using Mono.Cecil;

namespace RunesmithHub.Tool.Analysis.Isolation;

/// <summary>Recognises references to the .NET base library: framework assembly names signed with one of Microsoft's framework keys.</summary>
internal static class BaseLibrary
{
    private static readonly string[] ExactNames = ["mscorlib", "netstandard", "System", "Microsoft.CSharp", "Microsoft.VisualBasic"];
    private static readonly string[] Prefixes = ["System.", "Microsoft.Win32.", "Microsoft.VisualBasic."];
    private static readonly string[] FrameworkKeyTokens = ["b03f5f7f11d50a3a", "b77a5c561934e089", "cc7b13ffcd2ddd51", "7cec85d7bea7798e", "adb9793829ddae60"];

    public static bool Contains(AssemblyNameReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var name = reference.Name;
        var isFrameworkName = ExactNames.Contains(name, StringComparer.Ordinal) || Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
        var token = reference.PublicKeyToken is { Length: > 0 } bytes ? Convert.ToHexStringLower(bytes) : "";
        return isFrameworkName && FrameworkKeyTokens.Contains(token, StringComparer.Ordinal);
    }
}
