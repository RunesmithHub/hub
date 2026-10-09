using System.Text;

namespace RunesmithHub.Tool.Registrations;

/// <summary>How a check went: failures block the pull request, warnings ask the reviewer to look.</summary>
internal enum CheckLevel
{
    Pass,
    Warn,
    Fail,
}

/// <summary>One check of one file.</summary>
internal sealed record CheckLine(CheckLevel Level, string File, string Check, string Message);

/// <summary>The registration check's findings.</summary>
internal sealed class ValidationReport
{
    private readonly List<CheckLine> _lines = [];

    public IReadOnlyList<CheckLine> Lines => _lines;

    public bool Failed => _lines.Any(line => line.Level == CheckLevel.Fail);

    public void Add(CheckLevel level, string file, string check, string message) => _lines.Add(new CheckLine(level, file, check, message));

    public void Pass(string file, string check, string message) => Add(CheckLevel.Pass, file, check, message);

    public void Warn(string file, string check, string message) => Add(CheckLevel.Warn, file, check, message);

    public void Fail(string file, string check, string message) => Add(CheckLevel.Fail, file, check, message);

    /// <summary>Writes the report grouped by file, failures first in each group.</summary>
    public string Render()
    {
        if (_lines.Count == 0)
            return "No publisher or registration files changed.\n";
        var text = new StringBuilder();
        foreach (var file in _lines.GroupBy(line => line.File))
        {
            text.Append(file.Key).Append('\n');
            foreach (var line in file.OrderByDescending(line => line.Level))
                text.Append("  ").Append(line.Level switch { CheckLevel.Fail => "FAIL", CheckLevel.Warn => "WARN", _ => "PASS" }).Append("  ").Append(line.Check).Append(": ").Append(line.Message).Append('\n');
        }

        var failures = _lines.Count(line => line.Level == CheckLevel.Fail);
        var warnings = _lines.Count(line => line.Level == CheckLevel.Warn);
        text.Append('\n').Append(failures == 0 ? "The registration check passed" : $"The registration check failed with {failures} problem{(failures == 1 ? "" : "s")}")
            .Append(warnings == 0 ? "." : $"; {warnings} warning{(warnings == 1 ? "" : "s")} for the reviewer.").Append('\n');
        return text.ToString();
    }
}
