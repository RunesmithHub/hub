using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RunesmithHub.Tool.Analysis.Detection;

namespace RunesmithHub.Tool.Analysis.Heuristics;

/// <summary>A string literal and where it is.</summary>
internal sealed record ScannedText(string File, string Location, string Text);

/// <summary>A resource or package file and where it is.</summary>
internal sealed record ScannedBlob(string File, string Location, byte[] Bytes);

/// <summary>Everything in a package the heuristic rules look at, collected once.</summary>
internal sealed class ScannedMaterial
{
    private const string LibFolder = "lib/";

    public List<ScannedText> Strings { get; } = [];

    public List<ScannedBlob> Blobs { get; } = [];

    /// <summary>Gets the attribute and type names of each assembly file.</summary>
    public Dictionary<string, HashSet<string>> Names { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the APIs each assembly file references, such as <c>System.Diagnostics.Debugger.get_IsAttached</c>.</summary>
    public Dictionary<string, HashSet<string>> Apis { get; } = new(StringComparer.Ordinal);

    public static ScannedMaterial Collect(PackageSnapshot package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var material = new ScannedMaterial();
        foreach (var assembly in package.Assemblies)
            material.Add(assembly);

        var assemblyFiles = package.Assemblies.Select(assembly => LibFolder + assembly.File).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in package.Entries.Where(entry => !assemblyFiles.Contains(entry) && !package.NativeLibraries.Contains(entry)))
            material.Blobs.Add(new ScannedBlob(entry, entry, package.ReadFile(entry)));
        return material;
    }

    private void Add(LoadedAssembly assembly)
    {
        var file = LibFolder + assembly.File;
        var names = Names[file] = new HashSet<string>(StringComparer.Ordinal);
        var apis = Apis[file] = new HashSet<string>(StringComparer.Ordinal);
        AddAttributes(names, assembly.Definition);
        foreach (var module in assembly.Definition.Modules)
        {
            AddAttributes(names, module);
            foreach (var resource in module.Resources.OfType<EmbeddedResource>())
                Blobs.Add(new ScannedBlob(file, $"resource {resource.Name}", resource.GetResourceData()));
            foreach (var reference in module.GetMemberReferences())
                apis.Add(MemberNames.Of(reference));

            foreach (var type in module.GetTypes())
            {
                names.Add(type.Name);
                AddAttributes(names, type);
                foreach (var field in type.Fields.Where(field => field.HasConstant && field.Constant is string))
                    Strings.Add(new ScannedText(file, $"{type.FullName.Replace('/', '.')}.{field.Name}", (string)field.Constant));
                foreach (var method in type.Methods)
                {
                    AddAttributes(names, method);
                    if (!method.HasBody)
                        continue;
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode.Code == Code.Ldstr)
                            Strings.Add(new ScannedText(file, MemberNames.Of(method), (string)instruction.Operand));
                        else if (instruction.Operand is MethodReference called)
                            apis.Add(MemberNames.Of(called));
                    }
                }
            }
        }
    }

    private static void AddAttributes(HashSet<string> names, ICustomAttributeProvider provider)
    {
        foreach (var attribute in provider.CustomAttributes)
            names.Add(attribute.AttributeType.Name);
    }

    /// <summary>Gets a string's UTF-8 bytes, for entropy.</summary>
    public static byte[] Bytes(ScannedText text) => Encoding.UTF8.GetBytes(text.Text);
}
