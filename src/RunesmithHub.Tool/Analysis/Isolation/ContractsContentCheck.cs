using Mono.Cecil;

namespace RunesmithHub.Tool.Analysis.Isolation;

/// <summary>
/// Flags contracts that hold more than a public API needs: exported MEF parts, static mutable state, and methods with logic beyond what data
/// types, default interface members and attributes need. Small helpers are sometimes reasonable, so a person decides.
/// </summary>
internal static class ContractsContentCheck
{
    private const string CompilerGenerated = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";
    private static readonly string[] ExportAttributes = ["System.Composition.ExportAttribute", "System.ComponentModel.Composition.ExportAttribute"];

    public static IEnumerable<AnalysisProblem> Check(LoadedAssembly contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        foreach (var type in contracts.Definition.Modules.SelectMany(module => module.GetTypes()).Where(type => !IsGenerated(type)))
        {
            var name = type.FullName.Replace('/', '.');
            if (type.CustomAttributes.Any(attribute => ExportAttributes.Contains(attribute.AttributeType.FullName, StringComparer.Ordinal)))
                yield return AnalysisProblem.Review(ProblemCodes.ContractsContent, $"The contracts export the MEF part {name}; parts belong in the implementation.", name);

            foreach (var field in type.Fields.Where(field => field.IsStatic && !field.IsLiteral && !field.IsInitOnly && !IsGenerated(field)))
                yield return AnalysisProblem.Review(ProblemCodes.ContractsContent, $"The contracts hold static mutable state in {name}.{field.Name}.", $"{name}.{field.Name}");

            if (type.IsInterface)
                continue;
            var logic = type.Methods.Where(method => method.HasBody && !method.IsConstructor && !method.IsGetter && !method.IsSetter && !method.IsAddOn && !method.IsRemoveOn && !IsGenerated(method)).ToList();
            if (logic.Count > 0)
                yield return AnalysisProblem.Review(ProblemCodes.ContractsContent, $"The contracts type {name} has methods with logic: {string.Join(", ", logic.Select(method => method.Name).Distinct().Take(5))}.", name);
        }
    }

    private static bool IsGenerated(ICustomAttributeProvider member) =>
        member.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == CompilerGenerated)
        || (member is IMemberDefinition definition && definition.Name.Contains('<', StringComparison.Ordinal));
}
