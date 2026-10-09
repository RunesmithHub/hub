using Mono.Cecil;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Tool.Analysis.Api;

namespace RunesmithHub.Tool.Analysis.Isolation;

/// <summary>
/// Checks the boundary between a plugin's contracts and implementation: what each references, what the contracts' public API exposes,
/// who may see internals, and how the assemblies are named.
/// </summary>
internal static class IsolationCheck
{
    private const string InternalsVisibleTo = "System.Runtime.CompilerServices.InternalsVisibleToAttribute";
    private const string TestsSuffix = ".Tests";

    public static IReadOnlyList<AnalysisProblem> Check(PackageSnapshot package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Contracts is not { } contracts || package.Implementation is not { } implementation)
            return [];

        var manifest = package.Manifest;
        var problems = new List<AnalysisProblem>();
        CheckNames(contracts, implementation, problems);
        // Runesmith resolves a plugin's references only from the top of its lib folder.
        var bundled = package.Assemblies.Where(assembly => assembly.Role == AssemblyRole.Bundled && !assembly.File.Contains('/', StringComparison.Ordinal)).Select(assembly => assembly.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in contracts.Definition.MainModule.AssemblyReferences)
        {
            if (!IsShared(reference) && !IsDeclaredContracts(reference.Name, manifest))
                problems.Add(AnalysisProblem.Error(ProblemCodes.ContractsReference, ContractsReferenceMessage(reference.Name, contracts, implementation, manifest), contracts.File));
        }

        foreach (var reference in implementation.Definition.MainModule.AssemblyReferences)
        {
            if (IsShared(reference) || bundled.Contains(reference.Name) || string.Equals(reference.Name, contracts.Name, StringComparison.Ordinal))
                continue;
            var message = ContractsNames.IsContracts(reference.Name)
                ? IsDeclaredContracts(reference.Name, manifest) ? null : $"The implementation uses {reference.Name}, the contracts of a plugin it does not declare in dependencies."
                : $"The implementation references {reference.Name}, which is neither bundled, part of .NET, shared by Runesmith, nor a dependency's contracts.";
            if (message is not null)
                problems.Add(AnalysisProblem.Error(ProblemCodes.ImplementationReference, message, implementation.File));
        }

        CheckExposedTypes(contracts, manifest, problems);
        foreach (var assembly in (LoadedAssembly[])[contracts, implementation])
            CheckFriends(assembly, implementation.Name, package.Entries, problems);
        problems.AddRange(ContractsContentCheck.Check(contracts));
        return problems;
    }

    private static bool IsShared(AssemblyNameReference reference) => BaseLibrary.Contains(reference) || HostAssemblies.IsHostShared(reference.Name);

    private static bool IsDeclaredContracts(string assemblyName, PackagedManifest manifest) =>
        manifest.Dependencies.Any(dependency => ContractsNames.BelongsTo(assemblyName, dependency.Id));

    private static string ContractsReferenceMessage(string name, LoadedAssembly contracts, LoadedAssembly implementation, PackagedManifest manifest) =>
        string.Equals(name, implementation.Name, StringComparison.Ordinal) ? $"{contracts.Name} references the implementation; contracts must not."
        : ContractsNames.IsContracts(name) && !IsDeclaredContracts(name, manifest) ? $"{contracts.Name} uses {name}, the contracts of a plugin not declared in dependencies."
        : $"{contracts.Name} references {name}; contracts may only reference .NET, the Runesmith SDK and dependencies' contracts.";

    private static void CheckNames(LoadedAssembly contracts, LoadedAssembly implementation, List<AnalysisProblem> problems)
    {
        if (!string.Equals(contracts.Name, implementation.Name + ContractsNames.Suffix, StringComparison.Ordinal))
            problems.Add(AnalysisProblem.Error(ProblemCodes.AssemblyName, $"The contracts assembly is {contracts.Name}; it must be {implementation.Name}{ContractsNames.Suffix}.", contracts.File));
        foreach (var assembly in (LoadedAssembly[])[contracts, implementation])
        {
            if (!string.Equals(assembly.File, assembly.Name + ".dll", StringComparison.Ordinal))
                problems.Add(AnalysisProblem.Error(ProblemCodes.AssemblyName, $"lib/{assembly.File} holds the assembly {assembly.Name}; the file name must match.", assembly.File));
        }
    }

    private static void CheckExposedTypes(LoadedAssembly contracts, PackagedManifest manifest, List<AnalysisProblem> problems)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (member, type) in ExposedTypes(contracts.Definition))
        {
            if (type.Scope is not AssemblyNameReference scope || IsShared(scope) || (ContractsNames.IsContracts(scope.Name) && IsDeclaredContracts(scope.Name, manifest)))
                continue;
            if (reported.Add($"{member}|{type.FullName}"))
                problems.Add(AnalysisProblem.Error(ProblemCodes.ExposedType, $"{member} exposes {ApiNames.Of(type)} from {scope.Name}; contracts may only expose .NET, SDK and contracts types.", member));
        }
    }

    private static IEnumerable<(string Member, TypeReference Type)> ExposedTypes(AssemblyDefinition assembly)
    {
        foreach (var type in assembly.Modules.SelectMany(VisibleApi.Types))
        {
            var name = ApiNames.Of(type);
            IEnumerable<TypeReference> own =
            [
                .. type.BaseType is { } baseType ? [baseType] : Array.Empty<TypeReference>(),
                .. type.Interfaces.Select(item => item.InterfaceType),
                .. type.GenericParameters.SelectMany(parameter => parameter.Constraints.Select(constraint => constraint.ConstraintType)),
            ];
            foreach (var part in own.SelectMany(ApiNames.Parts))
                yield return (name, part);

            IEnumerable<(string, TypeReference)> members =
            [
                .. VisibleApi.Fields(type).Select(field => ($"{name}.{field.Name}", field.FieldType)),
                .. VisibleApi.Properties(type).Select(property => ($"{name}.{property.Name}", property.PropertyType)),
                .. VisibleApi.Events(type).Select(@event => ($"{name}.{@event.Name}", @event.EventType)),
                .. VisibleApi.Methods(type).SelectMany(method => (IEnumerable<(string, TypeReference)>)
                [
                    ($"{name}.{method.Name}", method.ReturnType),
                    .. method.Parameters.Select(parameter => ($"{name}.{method.Name}", parameter.ParameterType)),
                    .. method.GenericParameters.SelectMany(parameter => parameter.Constraints.Select(constraint => ($"{name}.{method.Name}", constraint.ConstraintType))),
                ]),
            ];
            foreach (var (member, memberType) in members)
            {
                foreach (var part in ApiNames.Parts(memberType))
                    yield return (member, part);
            }
        }
    }

    private static void CheckFriends(LoadedAssembly assembly, string root, IReadOnlyList<string> entries, List<AnalysisProblem> problems)
    {
        foreach (var attribute in assembly.Definition.CustomAttributes.Where(attribute => attribute.AttributeType.FullName == InternalsVisibleTo))
        {
            var friend = attribute.ConstructorArguments.FirstOrDefault().Value as string ?? "";
            var name = friend.Split(',')[0].Trim();
            var isOwnTests = name.StartsWith(root, StringComparison.Ordinal) && name.EndsWith(TestsSuffix, StringComparison.Ordinal)
                && !entries.Contains($"lib/{name}.dll", StringComparer.OrdinalIgnoreCase);
            if (!isOwnTests)
                problems.Add(AnalysisProblem.Error(ProblemCodes.InternalsVisibleTo, $"{assembly.Name} grants InternalsVisibleTo to {name}; only the plugin's own test assembly may see internals.", assembly.File));
        }
    }
}
