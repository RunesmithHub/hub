using System.CommandLine;
using Microsoft.Extensions.Time.Testing;
using RunesmithHub.Protocol.Updating;
using RunesmithHub.Tool.Console;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>Runs <c>rshub</c> commands in process with a fake clock, environment, terminal, GitHub and feed.</summary>
internal sealed class ToolRunner
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));

    public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

    public Queue<string?> Answers { get; } = new();

    /// <summary>Gets or sets what runs programs; the real processes when null.</summary>
    public IProcessRunner? Processes { get; set; }

    /// <summary>Gets or sets the handler that answers HTTP requests; the network when null.</summary>
    public HttpMessageHandler? Http { get; set; }

    public FakeGitHub GitHub { get; } = new();

    public FakeArtifactSource Artifacts { get; } = new();

    public FakeFeed Feed { get; } = new();

    public FakeAdvisories Advisories { get; } = new();

    public FakeDownloader Downloads { get; } = new();

    public async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var http = Http is null ? new HttpClient() : new HttpClient(Http, disposeHandler: false);
        var processes = Processes ?? new SystemProcessRunner();
        var services = new ToolServices
        {
            Processes = processes,
            Http = () => http,
            GitHub = () => GitHub,
            Bot = () => GitHub,
            Index = location => new DirectoryIndexFetcher(location),
            Artifacts = () => Artifacts,
            Feed = () => Feed,
            Advisories = () => Advisories,
            Downloads = () => Downloads,
            Git = directory => new GitCli(new SystemProcessRunner(), directory),
        };
        var context = new ToolContext(Clock, new QueuedPrompt(Answers), name => Environment.GetValueOrDefault(name), services);
        var exitCode = await HubCommandLine.Create(context).Parse(args).InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private sealed class QueuedPrompt(Queue<string?> answers) : IPassphrasePrompt
    {
        public string? Ask(string prompt) => answers.Count > 0 ? answers.Dequeue() : null;
    }
}
