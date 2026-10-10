using System.Text;
using Linguini.Syntax.Ast;
using Linguini.Syntax.Parser;

namespace RunesmithHub.Tool.Analysis.Localization;

/// <summary>A Fluent file read with Linguini's parser: the IDs of the messages it defines, and what is wrong with it, if anything.</summary>
/// <param name="Problem">The end of a sentence that starts with the file's name, such as "is not UTF-8 text.", or null when the file parses.</param>
internal sealed record FluentFile(IReadOnlyList<string> Messages, string? Problem)
{
    /// <summary>The most <c>{</c> and <c>(</c> characters a file may hold, which bounds how deeply the parser nests.</summary>
    public const int MaxOpenings = 10_000;

    // Linguini parses nested placeables and calls recursively, with about 1.5 KB of stack for each level.
    private const int StackSize = 64 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Parses the files, in order, as untrusted text.</summary>
    public static IReadOnlyList<FluentFile> Parse(IReadOnlyList<byte[]> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        var files = new FluentFile[contents.Count];
        var thread = new Thread(() =>
        {
            for (var i = 0; i < contents.Count; i++)
                files[i] = Parse(contents[i]);
        }, StackSize);
        thread.Start();
        thread.Join();
        return files;
    }

    private static FluentFile Parse(byte[] content)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return new FluentFile([], "is not UTF-8 text.");
        }

        if (text.Count(c => c is '{' or '(') > MaxOpenings)
            return new FluentFile([], $"holds more than {MaxOpenings} {{ and ( characters, the most the hub reads in one file; split it into smaller files.");

        Resource resource;
        try
        {
            // Linguini never returns from some unfinished last lines, such as an unclosed string, unless a line break follows them.
            resource = LinguiniParser.FromFragment((text.StartsWith('\uFEFF') ? text[1..] : text) + (text.EndsWith('\n') ? "" : "\n")).Parse();
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or ArgumentException or InvalidOperationException)
        {
            return new FluentFile([], "can't be read by the Fluent parser.");
        }

        var messages = resource.Entries.OfType<AstMessage>().Select(message => message.GetId()).ToList();
        if (resource.Errors.Count == 0)
            return new FluentFile(messages, null);
        var first = resource.Errors[0];
        var where = resource.Errors.Count == 1 ? "has a Fluent syntax error" : $"has {resource.Errors.Count} Fluent syntax errors, the first";
        return new FluentFile(messages, $"{where} on line {first.Row}: {Sentence(first.Message)}");
    }

    private static string Sentence(string message)
    {
        var words = string.Join(' ', TranslationCheck.Printable(message).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return words.EndsWith('.') ? words : words + ".";
    }
}
