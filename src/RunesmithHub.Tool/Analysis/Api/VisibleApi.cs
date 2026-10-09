using Mono.Cecil;

namespace RunesmithHub.Tool.Analysis.Api;

/// <summary>The types and members other assemblies can see: public ones, and protected ones of visible types.</summary>
internal static class VisibleApi
{
    public static IEnumerable<TypeDefinition> Types(ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return module.Types.Where(type => type.IsPublic).SelectMany(WithNested);
    }

    public static bool IsVisible(MethodDefinition method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    public static bool IsVisible(FieldDefinition field) => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

    public static IEnumerable<MethodDefinition> Methods(TypeDefinition type) => type.Methods.Where(IsVisible);

    public static IEnumerable<FieldDefinition> Fields(TypeDefinition type) => type.Fields.Where(IsVisible);

    public static IEnumerable<PropertyDefinition> Properties(TypeDefinition type) =>
        type.Properties.Where(property => (property.GetMethod is { } get && IsVisible(get)) || (property.SetMethod is { } set && IsVisible(set)));

    public static IEnumerable<EventDefinition> Events(TypeDefinition type) =>
        type.Events.Where(@event => (@event.AddMethod is { } add && IsVisible(add)) || (@event.RemoveMethod is { } remove && IsVisible(remove)));

    private static IEnumerable<TypeDefinition> WithNested(TypeDefinition type) =>
        [type, .. type.NestedTypes.Where(nested => nested.IsNestedPublic || nested.IsNestedFamily || nested.IsNestedFamilyOrAssembly).SelectMany(WithNested)];
}
