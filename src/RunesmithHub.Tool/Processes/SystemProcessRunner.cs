using System.ComponentModel;
using System.Diagnostics;

namespace RunesmithHub.Tool.Processes;

/// <summary>Runs programs as child processes, without a shell.</summary>
internal sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(program)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new ToolException($"Could not start {program}: {exception.Message}", exception);
        }

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }
}
