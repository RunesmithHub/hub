namespace RunesmithHub.Tool;

/// <summary>Sets step outputs for GitHub Actions by appending to the file <c>GITHUB_OUTPUT</c> names, when it is set.</summary>
internal static class ActionsOutput
{
    /// <summary>Sets a single-line output; line breaks are not allowed in the value.</summary>
    public static void Set(ToolContext context, string name, string value)
    {
        if (value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal))
            throw new ToolException($"The output {name} must be one line.");
        if (context.GetEnvironmentVariable("GITHUB_OUTPUT") is { Length: > 0 } path)
            File.AppendAllText(path, $"{name}={value}\n");
    }
}
