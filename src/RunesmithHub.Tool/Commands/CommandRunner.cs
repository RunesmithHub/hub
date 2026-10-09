using System.CommandLine;

namespace RunesmithHub.Tool.Commands;

/// <summary>Runs a command's work and turns problems the user can fix into a message and exit code 1.</summary>
internal static class CommandRunner
{
    public static async Task<int> RunAsync(ParseResult result, Func<TextWriter, Task<int>> work)
    {
        var output = result.InvocationConfiguration.Output;
        try
        {
            return await work(output).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ToolException or IOException or UnauthorizedAccessException or InvalidDataException
            or System.Security.Cryptography.CryptographicException or Protocol.Packaging.PackageException or GitHub.GitHubException
            or HttpRequestException)
        {
            await result.InvocationConfiguration.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
    }

    public static Task<int> Run(ParseResult result, Func<TextWriter, int> work) => RunAsync(result, output => Task.FromResult(work(output)));
}
