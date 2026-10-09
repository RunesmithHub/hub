using Mono.Cecil;

namespace RunesmithHub.Tool.Analysis;

/// <summary>What an assembly is to the plugin that ships it.</summary>
internal enum AssemblyRole
{
    Contracts,
    Implementation,
    Bundled,
}

/// <summary>An assembly from a package, read as metadata only; it is never loaded into the runtime.</summary>
/// <param name="File">Its path under <c>lib/</c>, such as <c>Lumen.Todo.dll</c>.</param>
internal sealed record LoadedAssembly(string File, AssemblyRole Role, AssemblyDefinition Definition)
{
    public string Name => Definition.Name.Name;
}
