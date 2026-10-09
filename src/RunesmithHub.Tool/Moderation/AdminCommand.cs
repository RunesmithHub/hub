using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Tool.Moderation;

/// <summary>An administrator command, as typed in a comment.</summary>
internal enum CommandVerb
{
    Approve,
    Reject,
    Review,
    Freeze,
    Unfreeze,
    Block,
    Unblock,
    Delete,
    Verify,
    Unverify,
    Suspend,
    Unsuspend,
    Advisory,
}

/// <summary>A parsed command: what to do, to what, why, and the public note.</summary>
/// <param name="Text">The command line as typed, for the pull request description.</param>
internal sealed record AdminCommand(CommandVerb Verb, ModerationTarget? Target, ModerationReason? Reason, string Note, string Text);

/// <summary>Parses the first line of a comment that starts with <c>/</c>.</summary>
internal static class CommandParser
{
    public const string Usage = """
        Commands: `/approve`, `/reject <reason>`, `/review`, `/freeze <target> <reason> [note]`, `/unfreeze <target>`, `/block <target> <reason> [note]`,
        `/unblock <target>`, `/delete <target> [note]`, `/verify <plugin>`, `/unverify <plugin> [note]`, `/suspend publisher:<name> [note]`,
        `/unsuspend publisher:<name>`, `/advisory <target>`. A target is `plugin.id`, `plugin.id@1.4.0`, `plugin.id@>=1.3.0 <1.4.2` or `publisher:name`.
        Reasons: malicious, compromised account, vulnerability, broken, policy, legal, developer request.
        """;

    private static readonly Dictionary<string, CommandVerb> Verbs = Enum.GetValues<CommandVerb>()
        .ToDictionary(verb => "/" + verb.ToString().ToLowerInvariant(), verb => verb, StringComparer.Ordinal);

    /// <summary>Finds and parses the command in a comment, or returns null when the comment has none.</summary>
    /// <exception cref="ToolException">The command is malformed; the message explains the usage.</exception>
    public static AdminCommand? Parse(string comment)
    {
        var line = comment.ReplaceLineEndings("\n").Split('\n').Select(text => text.Trim()).FirstOrDefault(text => text.StartsWith('/'));
        if (line is null)
            return null;

        var tokens = new Queue<string>(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var name = tokens.Dequeue();
        if (!Verbs.TryGetValue(name, out var verb))
            throw new ToolException($"`{name}` is not a hub command.");

        return verb switch
        {
            CommandVerb.Approve or CommandVerb.Review => new AdminCommand(verb, null, null, Rest(tokens), line),
            CommandVerb.Reject => new AdminCommand(verb, null, null, Required(Rest(tokens), "/reject needs a reason."), line),
            CommandVerb.Freeze or CommandVerb.Block => new AdminCommand(verb, Target(tokens, line), Reason(tokens) ?? throw new ToolException($"{name} needs a reason."), Rest(tokens), line),
            CommandVerb.Unfreeze or CommandVerb.Unblock or CommandVerb.Advisory or CommandVerb.Unsuspend => new AdminCommand(verb, Target(tokens, line), null, Rest(tokens), line),
            CommandVerb.Delete => new AdminCommand(verb, Target(tokens, line), null, Rest(tokens), line),
            CommandVerb.Verify or CommandVerb.Unverify => new AdminCommand(verb, PluginTarget(tokens, name), null, Rest(tokens), line),
            _ => new AdminCommand(verb, Target(tokens, line), null, Rest(tokens), line),
        };
    }

    /// <summary>Parses a target: <c>publisher:name</c>, <c>plugin.id</c>, or <c>plugin.id@range</c> where the range may span several words.</summary>
    public static ModerationTarget Target(Queue<string> tokens, string line)
    {
        if (!tokens.TryDequeue(out var first))
            throw new ToolException($"`{line}` needs a target.");
        if (first.StartsWith("publisher:", StringComparison.Ordinal))
        {
            var publisher = first["publisher:".Length..];
            return PublisherName.IsValid(publisher)
                ? new ModerationTarget { Kind = ModerationTargetKind.Publisher, Publisher = publisher }
                : throw new ToolException($"`{publisher}` is not a publisher name.");
        }

        var at = first.IndexOf('@', StringComparison.Ordinal);
        var plugin = at < 0 ? first : first[..at];
        if (!PluginId.IsValid(plugin))
            throw new ToolException($"`{plugin}` is not a plugin ID.");
        if (at < 0)
            return new ModerationTarget { Kind = ModerationTargetKind.Plugin, Plugin = plugin };

        var range = new List<string> { first[(at + 1)..] };
        while (tokens.TryPeek(out var next) && IsRangePart(next))
            range.Add(tokens.Dequeue());
        var text = string.Join(' ', range.Where(part => part.Length > 0));
        return VersionRange.TryParse(text, out var parsed, out var error)
            ? new ModerationTarget { Kind = ModerationTargetKind.Versions, Plugin = plugin, Range = parsed }
            : throw new ToolException($"`{text}` is not a version range: {error}");
    }

    private static ModerationTarget PluginTarget(Queue<string> tokens, string name) =>
        tokens.TryDequeue(out var plugin) && PluginId.IsValid(plugin)
            ? new ModerationTarget { Kind = ModerationTargetKind.Plugin, Plugin = plugin }
            : throw new ToolException($"{name} needs a plugin ID.");

    /// <summary>Reads a reason category; two-word reasons may be written with a space or a hyphen.</summary>
    private static ModerationReason? Reason(Queue<string> tokens)
    {
        if (!tokens.TryDequeue(out var first))
            return null;
        var word = first.ToLowerInvariant();
        if (word is "compromised" or "developer" && tokens.TryPeek(out var second))
        {
            tokens.Dequeue();
            word = $"{word}-{second.ToLowerInvariant()}";
        }

        return ParseReason(word) ?? throw new ToolException($"`{first}` is not a reason. {CommandParser.Usage}");
    }

    /// <summary>Parses a reason category written in kebab case, such as <c>compromised-account</c>, or returns null.</summary>
    public static ModerationReason? ParseReason(string text) => text.ToLowerInvariant() switch
    {
        "malicious" => ModerationReason.Malicious,
        "compromised-account" => ModerationReason.CompromisedAccount,
        "vulnerability" => ModerationReason.Vulnerability,
        "broken" => ModerationReason.Broken,
        "policy" => ModerationReason.Policy,
        "legal" => ModerationReason.Legal,
        "developer-request" => ModerationReason.DeveloperRequest,
        _ => null,
    };

    private static bool IsRangePart(string token) =>
        token == "||" || token.Length > 0 && (char.IsAsciiDigit(token[0]) || token[0] is '>' or '<' or '=' or '^' or '~');

    private static string Rest(Queue<string> tokens) => string.Join(' ', tokens);

    private static string Required(string text, string message) => text.Length > 0 ? text : throw new ToolException(message);
}
