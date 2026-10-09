using Mono.Cecil;

namespace RunesmithHub.Tool.Analysis.Detection;

/// <summary>
/// An API whose use means a capability: every member of a type, one member, or every type in a namespace when <see cref="Type"/> ends in
/// <c>.*</c>.
/// </summary>
internal sealed record CapabilityApi(string Capability, string Type, string? Member = null, ApiCondition Condition = ApiCondition.None)
{
    private bool IsNamespace => Type.EndsWith(".*", StringComparison.Ordinal);

    /// <summary>Gets whether a called method matches; <paramref name="typeName"/> is its declaring type's name without generic arguments.</summary>
    public bool Matches(string typeName, MethodReference method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var typeMatches = IsNamespace
            ? typeName.StartsWith(Type[..^1], StringComparison.Ordinal)
            : string.Equals(typeName, Type, StringComparison.Ordinal);
        if (!typeMatches || (Member is not null && !string.Equals(method.Name, Member, StringComparison.Ordinal)))
            return false;
        return Condition switch
        {
            ApiCondition.FirstParameterIsString => method.Parameters.Count > 0 && method.Parameters[0].ParameterType.MetadataType == MetadataType.String,
            _ => true,
        };
    }
}
