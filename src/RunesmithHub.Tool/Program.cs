using RunesmithHub.Tool;
using RunesmithHub.Tool.Console;

var writers = ActionsLogWriter.InstallForConsole(Environment.GetEnvironmentVariable);
try
{
    var context = new ToolContext(TimeProvider.System, new ConsolePassphrasePrompt(), Environment.GetEnvironmentVariable,
        ToolServices.Create(Environment.GetEnvironmentVariable, TimeProvider.System));
    return await HubCommandLine.Create(context).Parse(args).InvokeAsync().ConfigureAwait(false);
}
finally
{
    foreach (var writer in writers)
        writer.Complete();
}
