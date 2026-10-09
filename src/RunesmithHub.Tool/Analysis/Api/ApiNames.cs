using Mono.Cecil;

namespace RunesmithHub.Tool.Analysis.Api;

/// <summary>Writes type references as API signatures show them, with generic parameters by position so renaming one changes nothing.</summary>
internal static class ApiNames
{
    public static string Of(TypeReference type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type switch
        {
            GenericParameter parameter => parameter.Type == GenericParameterType.Method ? $"!!{parameter.Position}" : $"!{parameter.Position}",
            GenericInstanceType generic => $"{Of(generic.ElementType)}<{string.Join(", ", generic.GenericArguments.Select(Of))}>",
            ArrayType array => $"{Of(array.ElementType)}[{new string(',', array.Rank - 1)}]",
            ByReferenceType reference => Of(reference.ElementType) + "&",
            PointerType pointer => Of(pointer.ElementType) + "*",
            IModifierType modifier => Of(modifier.ElementType),
            _ => type.FullName.Replace('/', '.'),
        };
    }

    /// <summary>Gets every type a reference is built from: itself, the elements of arrays and the arguments of generics.</summary>
    public static IEnumerable<TypeReference> Parts(TypeReference type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type switch
        {
            GenericParameter => [],
            GenericInstanceType generic => [.. Parts(generic.ElementType), .. generic.GenericArguments.SelectMany(Parts)],
            TypeSpecification specification => Parts(specification.ElementType),
            _ => [type],
        };
    }
}
