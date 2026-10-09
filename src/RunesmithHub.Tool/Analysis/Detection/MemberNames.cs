using Mono.Cecil;

namespace RunesmithHub.Tool.Analysis.Detection;

/// <summary>Names members as their source names them, folding compiler-generated state machines and lambdas into the method they came from.</summary>
internal static class MemberNames
{
    public static string Of(MethodDefinition method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var type = method.DeclaringType;
        var name = SourceName(method.Name) ?? method.Name;
        while (type.IsNested && type.Name.StartsWith('<'))
        {
            name = SourceName(type.Name) is { Length: > 0 } outer && method.Name is "MoveNext" or "SetStateMachine" ? outer : name;
            type = type.DeclaringType;
        }

        return $"{type.FullName.Replace('/', '.')}.{name}";
    }

    /// <summary>Gets the API a reference names, such as <c>System.Net.Http.HttpClient.GetAsync</c>.</summary>
    public static string Of(MemberReference member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return $"{CapabilityApiTable.TypeName(member.DeclaringType)}.{member.Name}";
    }

    private static string? SourceName(string generatedName)
    {
        if (!generatedName.StartsWith('<'))
            return null;
        var end = generatedName.IndexOf('>', StringComparison.Ordinal);
        return end > 1 ? generatedName[1..end] : null;
    }
}
