using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>Compiles C# source to an assembly in memory, against the running .NET and any given assemblies.</summary>
internal static class Compiler
{
    private static readonly Lazy<MetadataReference[]> Framework = new(() =>
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(path) is "netstandard.dll" or "mscorlib.dll" or "Microsoft.Win32.Registry.dll" or "Microsoft.Win32.Primitives.dll")
            .Select(path => MetadataReference.CreateFromFile(path))]);

    /// <exception cref="InvalidOperationException">The source does not compile.</exception>
    public static byte[] Compile(string assemblyName, string source, params byte[][] references) => Compile(assemblyName, source, unsafeCode: false, references);

    public static byte[] Compile(string assemblyName, string source, bool unsafeCode, params byte[][] references)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            [.. Framework.Value, .. references.Select(reference => MetadataReference.CreateFromImage(reference))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: unsafeCode, nullableContextOptions: NullableContextOptions.Enable, deterministic: true));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success)
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        return output.ToArray();
    }

    /// <summary>Compiles source into an assembly that embeds one resource.</summary>
    public static byte[] CompileWithResource(string assemblyName, string source, string resourceName, byte[] resource)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName, [CSharpSyntaxTree.ParseText(source)], Framework.Value, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));
        using var output = new MemoryStream();
        var result = compilation.Emit(output, manifestResources: [new ResourceDescription(resourceName, () => new MemoryStream(resource), isPublic: true)]);
        return result.Success ? output.ToArray() : throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
    }

    /// <summary>Compiles a stub of the Runesmith SDK with the secret store and the launcher.</summary>
    public static byte[] SdkStub(string version = "0.1.0") => Compile("Runesmith.Sdk", $$"""
        [assembly: System.Reflection.AssemblyVersion("{{version}}.0")]
        namespace Runesmith.Sdk.Shell
        {
            public interface ISecretStore { System.Threading.Tasks.Task<string?> GetAsync(string key); }
            public interface ILauncher { void Open(string target); }
        }
        """);
}
