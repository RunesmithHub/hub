using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>Answers program runs from canned results by their command line, such as <c>git rev-parse HEAD</c>, and records every run.</summary>
internal sealed class FakeProcesses : IProcessRunner
{
    public Dictionary<string, ProcessResult> Results { get; } = new(StringComparer.Ordinal);

    public List<string> Runs { get; } = [];

    public FakeProcesses Answer(string commandLine, string output, int exitCode = 0)
    {
        Results[commandLine] = new ProcessResult(exitCode, output, exitCode == 0 ? "" : output);
        return this;
    }

    public Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken = default)
    {
        var commandLine = string.Join(' ', [program, .. arguments]);
        Runs.Add(commandLine);
        return Task.FromResult(Results.TryGetValue(commandLine, out var result) ? result : new ProcessResult(1, "", $"No canned result for {commandLine}"));
    }
}
