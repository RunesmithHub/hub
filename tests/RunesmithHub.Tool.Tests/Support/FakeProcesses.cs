using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>Answers program runs from canned results by their command line, such as <c>git rev-parse HEAD</c>, and records every run.</summary>
internal sealed class FakeProcesses : IProcessRunner
{
    public Dictionary<string, ProcessResult> Results { get; } = new(StringComparer.Ordinal);

    public List<string> Runs { get; } = [];

    /// <summary>Gets the environment variables each run set or removed, in the order of <see cref="Runs"/>.</summary>
    public List<IReadOnlyDictionary<string, string?>> Environments { get; } = [];

    /// <summary>Gets the working folder of each run, in the order of <see cref="Runs"/>.</summary>
    public List<string> WorkingDirectories { get; } = [];

    /// <summary>Gets or sets what runs whose command line starts with a prefix do besides answering, such as writing a file.</summary>
    public Dictionary<string, Action<IReadOnlyList<string>>> SideEffects { get; } = new(StringComparer.Ordinal);

    /// <summary>Makes <c>git archive</c> succeed and write a small file where its <c>-o</c> option says.</summary>
    public FakeProcesses WritesArchives()
    {
        SideEffects["git archive"] = arguments => File.WriteAllText(arguments[arguments.ToList().IndexOf("-o") + 1], "tar");
        return this;
    }

    public FakeProcesses Answer(string commandLine, string output, int exitCode = 0)
    {
        Results[commandLine] = new ProcessResult(exitCode, output, exitCode == 0 ? "" : output);
        return this;
    }

    public Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken = default) =>
        RunAsync(program, arguments, workingDirectory, new Dictionary<string, string?>(), cancellationToken);

    public Task<ProcessResult> RunAsync(
        string program, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken = default)
    {
        var commandLine = string.Join(' ', [program, .. arguments]);
        Runs.Add(commandLine);
        Environments.Add(environment);
        WorkingDirectories.Add(workingDirectory);
        foreach (var (prefix, effect) in SideEffects)
        {
            if (commandLine.StartsWith(prefix, StringComparison.Ordinal))
            {
                effect(arguments);
                return Task.FromResult(new ProcessResult(0, "", ""));
            }
        }

        return Task.FromResult(Results.TryGetValue(commandLine, out var result) ? result : new ProcessResult(1, "", $"No canned result for {commandLine}"));
    }
}
