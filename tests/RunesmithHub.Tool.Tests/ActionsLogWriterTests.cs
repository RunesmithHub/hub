namespace RunesmithHub.Tool.Tests;

public sealed class ActionsLogWriterTests
{
    private static string Write(Action<TextWriter> write)
    {
        var inner = new StringWriter();
        var writer = new ActionsLogWriter(inner);
        write(writer);
        writer.Complete();
        return inner.ToString();
    }

    [Theory]
    [InlineData("::error::text", ": :error::text")]
    [InlineData("  ::add-mask::x", "  : :add-mask::x")]
    [InlineData("\t##[group]x", "\t# #[group]x")]
    [InlineData("a ::error::text", "a ::error::text")]
    [InlineData(":not a command", ":not a command")]
    [InlineData("##not a command", "##not a command")]
    public void NeutralizesLinesTheRunnerWouldReadAsCommands(string line, string expected) =>
        Assert.Equal(expected, ActionsLogWriter.Neutralize(line));

    [Fact]
    public void NeutralizesEveryLineOfWrittenText() =>
        Assert.Equal("first\n: :stop-commands::t\r: :warning::w\nlast\n", Write(writer => writer.Write("first\n::stop-commands::t\r::warning::w\nlast\n")));

    [Fact]
    public void NeutralizesACommandWrittenOneCharacterAtATime() =>
        Assert.Equal(": :error::x\n", Write(writer =>
        {
            foreach (var c in "::error::x\n")
            {
                writer.Write(c);
                writer.Flush();
            }
        }));

    [Fact]
    public void EndsAnUnfinishedLineThatCouldStillBecomeACommand() =>
        Assert.Equal("text\n:\n", Write(writer => writer.Write("text\n:")));

    [Fact]
    public void LeavesOtherTextAsItIs() =>
        Assert.Equal("{\n  \"id\": \"a::b\"\n}", Write(writer => writer.Write("{\n  \"id\": \"a::b\"\n}")));
}
