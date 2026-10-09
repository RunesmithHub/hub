namespace RunesmithHub.Tool.Processes;

/// <summary>How a program ended and what it wrote.</summary>
internal sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>Returns the output, or throws with the program's error text when it failed.</summary>
    /// <exception cref="ToolException">The program failed.</exception>
    public string OutputOrThrow(string what) =>
        Succeeded ? Output : throw new ToolException($"{what} failed with exit code {ExitCode}: {(Error.Length > 0 ? Error : Output).Trim()}");
}
