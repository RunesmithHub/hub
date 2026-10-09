using System.Globalization;
using Mono.Cecil;

namespace RunesmithHub.Tool.Analysis.Api;

/// <summary>One visible type or member: what identifies it, and everything about it a dependent compiles or runs against.</summary>
/// <param name="AddsObligation">Whether adding it breaks types that implement or derive from its type, as an abstract member does.</param>
internal sealed record ApiEntry(string Key, string Signature, bool AddsObligation);

/// <summary>The public API of an assembly as a set of signatures, so two versions can be compared.</summary>
internal sealed class ApiSurface
{
    private ApiSurface(IReadOnlyDictionary<string, ApiEntry> entries) => Entries = entries;

    public IReadOnlyDictionary<string, ApiEntry> Entries { get; }

    public static ApiSurface Read(AssemblyDefinition assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var entries = new Dictionary<string, ApiEntry>(StringComparer.Ordinal);
        foreach (var type in assembly.Modules.SelectMany(VisibleApi.Types))
        {
            var typeName = ApiNames.Of(type);
            var extensible = (type.IsInterface || type.IsAbstract) && !type.IsSealed && HasVisibleConstructorOrIsInterface(type);
            Add(entries, new ApiEntry(typeName, TypeSignature(type), false));
            foreach (var field in VisibleApi.Fields(type).Where(field => !field.IsSpecialName))
                Add(entries, new ApiEntry($"{typeName}.{field.Name}", FieldSignature(field), false));
            foreach (var method in VisibleApi.Methods(type).Where(method => !IsAccessor(method)))
                Add(entries, new ApiEntry(MethodKey(typeName, method), MethodSignature(method), extensible && method.IsAbstract));
            foreach (var property in VisibleApi.Properties(type))
                Add(entries, new ApiEntry($"{typeName}.{property.Name}{Indexer(property)}", PropertySignature(property), extensible && (property.GetMethod ?? property.SetMethod)?.IsAbstract == true));
            foreach (var @event in VisibleApi.Events(type))
                Add(entries, new ApiEntry($"{typeName}.{@event.Name}", $"event {ApiNames.Of(@event.EventType)}", false));
        }

        return new ApiSurface(entries);
    }

    private static bool IsAccessor(MethodDefinition method) => method.IsGetter || method.IsSetter || method.IsAddOn || method.IsRemoveOn;

    private static void Add(Dictionary<string, ApiEntry> entries, ApiEntry entry) => entries.TryAdd(entry.Key, entry);

    private static bool HasVisibleConstructorOrIsInterface(TypeDefinition type) =>
        type.IsInterface || type.Methods.Any(method => method.IsConstructor && !method.IsStatic && VisibleApi.IsVisible(method));

    private static string TypeSignature(TypeDefinition type)
    {
        var kind = type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : IsDelegate(type) ? "delegate" : "class";
        var modifiers = new List<string>();
        if (type.IsAbstract && type.IsSealed && kind == "class")
            modifiers.Add("static");
        else if (type.IsAbstract && kind == "class")
            modifiers.Add("abstract");
        else if (type.IsSealed && kind == "class")
            modifiers.Add("sealed");

        var bases = new List<string>();
        if (type.BaseType is { } baseType && kind == "class")
            bases.Add(ApiNames.Of(baseType));
        if (type.IsEnum)
            bases.Add(ApiNames.Of(type.Fields.First(field => field.IsSpecialName).FieldType));
        bases.AddRange(type.Interfaces.Select(item => ApiNames.Of(item.InterfaceType)).Order(StringComparer.Ordinal));

        var signature = $"{Visibility(type)} {string.Join(' ', [.. modifiers, kind])} {ApiNames.Of(type)}{GenericParameters(type.GenericParameters)}";
        if (bases.Count > 0)
            signature += " : " + string.Join(", ", bases);
        if (IsDelegate(type) && type.Methods.FirstOrDefault(method => method.Name == "Invoke") is { } invoke)
            signature += $" = {ApiNames.Of(invoke.ReturnType)}({Parameters(invoke)})";
        return signature;
    }

    private static bool IsDelegate(TypeDefinition type) => type.BaseType?.FullName == "System.MulticastDelegate";

    private static string Visibility(TypeDefinition type) => type.IsPublic || type.IsNestedPublic ? "public" : "protected";

    private static string Visibility(MethodDefinition method) => method.IsPublic ? "public" : "protected";

    private static string FieldSignature(FieldDefinition field)
    {
        var modifiers = field.IsLiteral ? "const " : (field.IsStatic ? "static " : "") + (field.IsInitOnly ? "readonly " : "");
        var visibility = field.IsPublic ? "public" : "protected";
        var value = field.HasConstant ? $" = {Constant(field.Constant)}" : "";
        return $"{visibility} {modifiers}{ApiNames.Of(field.FieldType)} {field.Name}{value}";
    }

    private static string MethodKey(string typeName, MethodDefinition method) =>
        $"{typeName}.{method.Name}{(method.HasGenericParameters ? $"``{method.GenericParameters.Count}" : "")}({string.Join(", ", method.Parameters.Select(parameter => ApiNames.Of(parameter.ParameterType)))})";

    private static string MethodSignature(MethodDefinition method)
    {
        var modifiers = new List<string> { Visibility(method) };
        if (method.IsStatic)
            modifiers.Add("static");
        if (method.IsAbstract)
            modifiers.Add("abstract");
        else if (method.IsVirtual && !method.IsFinal)
            modifiers.Add("virtual");
        else if (method.IsVirtual && method.IsFinal && !method.DeclaringType.IsInterface)
            modifiers.Add("sealed");
        return $"{string.Join(' ', modifiers)} {ApiNames.Of(method.ReturnType)} {method.Name}{GenericParameters(method.GenericParameters)}({Parameters(method)})";
    }

    private static string PropertySignature(PropertyDefinition property)
    {
        static string Accessor(MethodDefinition? accessor, string name) =>
            accessor is not null && VisibleApi.IsVisible(accessor) ? $" {(accessor.IsPublic ? "" : "protected ")}{name};" : "";
        var setter = property.SetMethod?.ReturnType is IModifierType { ModifierType.Name: "IsExternalInit" } ? "init" : "set";
        var isStatic = (property.GetMethod ?? property.SetMethod)?.IsStatic == true ? "static " : "";
        return $"{isStatic}{ApiNames.Of(property.PropertyType)} {property.Name}{Indexer(property)} {{{Accessor(property.GetMethod, "get")}{Accessor(property.SetMethod, setter)} }}";
    }

    private static string Indexer(PropertyDefinition property) =>
        property.HasParameters ? $"[{string.Join(", ", property.Parameters.Select(parameter => ApiNames.Of(parameter.ParameterType)))}]" : "";

    private static string Parameters(MethodDefinition method) => string.Join(", ", method.Parameters.Select(parameter =>
    {
        var prefix = parameter.IsOut ? "out " : parameter.ParameterType.IsByReference && parameter.IsIn ? "in " : "";
        var value = parameter.HasConstant ? $" = {Constant(parameter.Constant)}" : parameter.IsOptional ? " = default" : "";
        return $"{prefix}{ApiNames.Of(parameter.ParameterType)}{value}";
    }));

    private static string GenericParameters(Mono.Collections.Generic.Collection<GenericParameter> parameters)
    {
        if (parameters.Count == 0)
            return "";
        return "<" + string.Join(", ", parameters.Select(parameter =>
        {
            var constraints = new List<string>();
            if (parameter.HasReferenceTypeConstraint)
                constraints.Add("class");
            if (parameter.HasNotNullableValueTypeConstraint)
                constraints.Add("struct");
            constraints.AddRange(parameter.Constraints.Select(constraint => ApiNames.Of(constraint.ConstraintType)).Order(StringComparer.Ordinal));
            if (parameter.HasDefaultConstructorConstraint && !parameter.HasNotNullableValueTypeConstraint)
                constraints.Add("new()");
            var variance = parameter.IsCovariant ? "out " : parameter.IsContravariant ? "in " : "";
            return constraints.Count == 0 ? $"{variance}{ApiNames.Of(parameter)}" : $"{variance}{ApiNames.Of(parameter)} : {string.Join(", ", constraints)}";
        })) + ">";
    }

    private static string Constant(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
