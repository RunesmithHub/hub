using System.CommandLine;
using System.Globalization;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Tool.Fixture;
using RunesmithHub.Tool.Moderation;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub dev</c>: tools for developing the hub, the website and Runesmith.</summary>
internal static class DevCommands
{
    public static Command Create(ToolContext context)
    {
        var dev = new Command("dev", "Tools for developing against the hub.");
        dev.Add(Fixture(context));
        dev.Add(Moderate(context));
        return dev;
    }

    private static Command Fixture(ToolContext context)
    {
        var directory = new Argument<string>("directory") { Description = "The empty folder to write the index to." };
        var expired = new Option<bool>("--expired") { Description = "Write a timestamp that has already expired." };
        var baseUrl = new Option<string>("--base-url") { Description = "The URL the folder will be served at, for package and image links.", DefaultValueFactory = _ => FixtureOptions.DefaultBaseUrl };
        var command = new Command("fixture", "Write a complete signed sample index, with throwaway test keys beside it.") { directory, expired, baseUrl };
        command.SetAction(result => CommandRunner.Run(result, writer =>
        {
            var options = new FixtureOptions(result.GetRequiredValue(directory), FixtureOptions.ParseBaseUrl(result.GetRequiredValue(baseUrl)), result.GetValue(expired));
            var summary = new FixtureWriter(context.Time).Write(options);
            writer.WriteLine($"Wrote a signed sample index with {summary.Plugins} plugins and {summary.Packages} packages to {options.Directory}.");
            writer.WriteLine($"Trusted root: {summary.TrustedRoot}");
            writer.WriteLine($"Test keys (never use them for anything real): {summary.KeysDirectory}");
            return 0;
        }));
        return command;
    }

    private static Command Moderate(ToolContext context)
    {
        const string targets = "publisher:name, plugin.id or plugin.id@range";
        var directory = new Argument<string>("directory") { Description = "The folder rshub dev fixture wrote." };
        var block = new Option<string>("--block") { Description = $"Block a target: {targets}." };
        var freeze = new Option<string>("--freeze") { Description = $"Freeze a target: {targets}." };
        var reason = new Option<string>("--reason") { Description = "The reason category, such as malicious or broken; malicious for a block and broken for a freeze by default." };
        var note = new Option<string>("--note") { Description = "The public note.", DefaultValueFactory = _ => "Set with rshub dev moderate." };
        var command = new Command("moderate", "Add a block or freeze to a fixture and sign its moderation list and timestamp again with its test keys.")
        {
            directory, block, freeze, reason, note,
        };
        command.SetAction(result => CommandRunner.Run(result, writer =>
        {
            var (action, text) = (result.GetValue(block), result.GetValue(freeze)) switch
            {
                ({ } blocked, null) => (ModerationAction.Block, blocked),
                (null, { } frozen) => (ModerationAction.Freeze, frozen),
                _ => throw new ToolException("Give either --block or --freeze."),
            };
            var tokens = new Queue<string>(text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var target = CommandParser.Target(tokens, text);
            if (tokens.Count > 0)
                throw new ToolException($"\"{text}\" is not a target: use {targets}.");
            var category = result.GetValue(reason) is { } given
                ? CommandParser.ParseReason(given) ?? throw new ToolException($"\"{given}\" is not a reason category.")
                : action == ModerationAction.Block ? ModerationReason.Malicious : ModerationReason.Broken;

            var moderation = new FixtureModerator(context.Time).Moderate(result.GetRequiredValue(directory), action, target, category, result.GetRequiredValue(note));
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Added entry {moderation.Entry.Id}: {action.ToString().ToLowerInvariant()} {target}. Moderation list version {moderation.ModerationVersion}, timestamp version {moderation.TimestampVersion}."));
            return 0;
        }));
        return command;
    }
}
