using System.CommandLine;

namespace RunesmithHub.Tool.Commands;

/// <summary>Options several commands share.</summary>
internal static class CommonOptions
{
    public static Option<string> Registry() =>
        new("--registry") { Description = "The registry repository's folder.", DefaultValueFactory = _ => "." };

    public static Option<string> Current() =>
        new("--current") { Description = "The deployed index: its https:// base URL or a local folder.", Required = true };

    public static Option<string> Index() =>
        new("--index") { Description = "The index directory, such as a checkout of the index branch; it is read and the new index written into it.", Required = true };

    public static Option<string> Output(string description) => new("--out") { Description = description, Required = true };

    public static Option<int> PullRequest() => new("--pr") { Description = "The pull request number.", Required = true };

    /// <summary>Gets the hub bot's login, <c>&lt;app-slug&gt;[bot]</c>, from <c>HUB_BOT_LOGIN</c>.</summary>
    public static string? BotLogin(ToolContext context) => context.GetEnvironmentVariable("HUB_BOT_LOGIN") is { Length: > 0 } login ? login : null;
}
