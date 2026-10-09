using Mono.Cecil;
using RunesmithHub.Tool.Analysis;

namespace RunesmithHub.Tool.Tests.Support;

internal static class Assemblies
{
    /// <summary>Reads compiled bytes as the analysis reads package assemblies.</summary>
    public static LoadedAssembly Load(byte[] image, AssemblyRole role = AssemblyRole.Implementation)
    {
        var definition = AssemblyDefinition.ReadAssembly(new MemoryStream(image), new ReaderParameters { ReadingMode = ReadingMode.Immediate, InMemory = true });
        return new LoadedAssembly(definition.Name.Name + ".dll", role, definition);
    }

    /// <summary>Compiles a method body into <c>Sample.Code.Run</c> and reads it.</summary>
    public static LoadedAssembly Method(string body, bool unsafeCode = false) =>
        Load(Compiler.Compile("Sample", $$"""
            using System;
            using System.Reflection;
            namespace Sample
            {
                public static class Code
                {
                    public static {{(unsafeCode ? "unsafe " : "")}}object? Run(Runesmith.Sdk.Shell.ISecretStore secrets, Runesmith.Sdk.Shell.ILauncher launcher)
                    {
                        {{body}}
                        return null;
                    }
                }
            }
            """, unsafeCode, Compiler.SdkStub()));
}
