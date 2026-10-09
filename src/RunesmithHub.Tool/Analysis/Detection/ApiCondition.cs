namespace RunesmithHub.Tool.Analysis.Detection;

/// <summary>A condition on the called method's parameters, for APIs whose capability depends on the overload.</summary>
internal enum ApiCondition
{
    None,

    /// <summary>The first parameter is a string, such as a path or a type name.</summary>
    FirstParameterIsString,
}
