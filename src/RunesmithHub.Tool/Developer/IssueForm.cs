using System.Globalization;
using System.Text.Json;

namespace RunesmithHub.Tool.Developer;

/// <summary>An issue opened from an issue form: its number, author, labels and the answers by field label.</summary>
internal sealed record IssueForm(int Number, string Title, long AuthorId, IReadOnlyList<string> Labels, IReadOnlyDictionary<string, string> Fields)
{
    private const string NoResponse = "_No response_";

    /// <summary>Gets an answer, or an empty string when the field was left empty.</summary>
    public string Field(string label) => Fields.GetValueOrDefault(label) ?? "";

    /// <summary>Reads the issue object GitHub sends with an <c>issues</c> event.</summary>
    /// <exception cref="ToolException">The file is not an issue.</exception>
    public static IssueForm Read(byte[] issueJson)
    {
        try
        {
            using var document = JsonDocument.Parse(issueJson);
            var issue = document.RootElement;
            return new IssueForm(
                issue.GetProperty("number").GetInt32(),
                issue.GetProperty("title").GetString() ?? "",
                issue.GetProperty("user").GetProperty("id").GetInt64(),
                [.. issue.GetProperty("labels").EnumerateArray().Select(label => label.GetProperty("name").GetString() ?? "")],
                ParseBody(issue.TryGetProperty("body", out var body) ? body.GetString() ?? "" : ""));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"The issue file is not a GitHub issue: {exception.Message}"), exception);
        }
    }

    /// <summary>Splits a form's body into its <c>### Label</c> sections.</summary>
    public static IReadOnlyDictionary<string, string> ParseBody(string body)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        string? label = null;
        var value = new List<string>();
        foreach (var line in body.ReplaceLineEndings("\n").Split('\n').Append("### "))
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                if (label is not null)
                {
                    var text = string.Join('\n', value).Trim();
                    fields[label] = text == NoResponse ? "" : text;
                }

                label = line[4..].Trim();
                value.Clear();
            }
            else
            {
                value.Add(line);
            }
        }

        return fields;
    }
}
