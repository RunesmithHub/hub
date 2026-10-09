namespace RunesmithHub.Tool.Processes;

/// <summary>Runs other programs, such as <c>git</c>, <c>dotnet</c> and <c>sleet</c>, so tests can replace them.</summary>
internal interface IProcessRunner
{
    /// <summary>Runs a program to completion, with arguments passed as a list and never through a shell, and captures what it writes.</summary>
    /// <exception cref="ToolException">The program could not be started.</exception>
    Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken = default);
}
