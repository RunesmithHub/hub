using RunesmithHub.Tool.Console;

namespace RunesmithHub.Tool;

/// <summary>What commands get from the outside world, so tests can replace it.</summary>
/// <param name="GetEnvironmentVariable">Reads an environment variable, or returns null when it is not set.</param>
internal sealed record ToolContext(TimeProvider Time, IPassphrasePrompt Prompt, Func<string, string?> GetEnvironmentVariable, ToolServices Services)
{
    /// <summary>Reads an environment variable that must be set.</summary>
    /// <exception cref="ToolException">It is not set.</exception>
    public string RequireEnvironmentVariable(string name) =>
        GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new ToolException($"The environment variable {name} is not set.");
}
