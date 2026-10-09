using RunesmithHub.Protocol.Index;
using RunesmithHub.Tool.Moderation;

namespace RunesmithHub.Tool.Tests.Moderation;

public sealed class CommandParserTests
{
    [Fact]
    public void FindsTheCommandLineInAComment()
    {
        var command = CommandParser.Parse("Thanks for the report.\n\n/freeze lumen.todo compromised account Installs are paused.\nMore text.")!;

        Assert.Equal(CommandVerb.Freeze, command.Verb);
        Assert.Equal(ModerationTargetKind.Plugin, command.Target!.Kind);
        Assert.Equal("lumen.todo", command.Target.Plugin);
        Assert.Equal(ModerationReason.CompromisedAccount, command.Reason);
        Assert.Equal("Installs are paused.", command.Note);
    }

    [Theory]
    [InlineData("/block lumen.todo@1.4.0 malicious", ModerationTargetKind.Versions, "1.4.0")]
    [InlineData("/block lumen.todo@>=1.3.0 <1.4.2 vulnerability", ModerationTargetKind.Versions, ">=1.3.0 <1.4.2")]
    [InlineData("/block lumen.todo@^1.3.0 || 2.0.0 developer-request", ModerationTargetKind.Versions, ">=1.3.0 <2.0.0 || 2.0.0")]
    [InlineData("/block publisher:fastcode malicious", ModerationTargetKind.Publisher, null)]
    public void ParsesEveryKindOfTarget(string text, ModerationTargetKind kind, string? range)
    {
        var command = CommandParser.Parse(text)!;

        Assert.Equal(kind, command.Target!.Kind);
        Assert.Equal(range, command.Target.Range?.ToString());
        Assert.NotNull(command.Reason);
    }

    [Fact]
    public void ReadsTheReasonOfARejection()
    {
        var command = CommandParser.Parse("/reject The icon is someone else's logo.")!;

        Assert.Equal(CommandVerb.Reject, command.Verb);
        Assert.Equal("The icon is someone else's logo.", command.Note);
    }

    [Fact]
    public void IgnoresCommentsWithoutACommand() => Assert.Null(CommandParser.Parse("Looks good to me."));

    [Theory]
    [InlineData("/obliterate lumen.todo", "not a hub command")]
    [InlineData("/freeze lumen.todo", "needs a reason")]
    [InlineData("/freeze lumen.todo grumpy", "not a reason")]
    [InlineData("/freeze Lumen.Todo broken", "not a plugin ID")]
    [InlineData("/block lumen.todo@>=nope malicious", "not a version range")]
    [InlineData("/reject", "needs a reason")]
    [InlineData("/verify publisher:lumen", "needs a plugin ID")]
    public void ExplainsMalformedCommands(string text, string message)
    {
        var exception = Assert.Throws<ToolException>(() => CommandParser.Parse(text));

        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }
}
