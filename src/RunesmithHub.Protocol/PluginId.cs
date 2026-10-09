using System.Diagnostics.CodeAnalysis;

namespace RunesmithHub.Protocol;

/// <summary>A plugin's ID, <c>&lt;publisher&gt;.&lt;name&gt;</c>, such as <c>lumen.todo</c>; both parts follow <see cref="PublisherName"/>'s rules.</summary>
public readonly record struct PluginId
{
    private PluginId(string publisher, string name)
    {
        Publisher = publisher;
        Name = name;
    }

    /// <summary>Gets the publisher part, before the dot.</summary>
    public string Publisher { get; }

    /// <summary>Gets the name part, after the dot.</summary>
    public string Name { get; }

    /// <exception cref="FormatException">The text is not a valid plugin ID.</exception>
    public static PluginId Parse(string text) =>
        TryParse(text, out var id) ? id : throw new FormatException($"\"{text}\" is not a valid plugin ID.");

    public static bool TryParse([NotNullWhen(true)] string? text, out PluginId id)
    {
        id = default;
        if (text is null)
            return false;

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0 || !PublisherName.IsValid(text[..dot]) || !PublisherName.IsValid(text[(dot + 1)..]))
            return false;

        id = new PluginId(text[..dot], text[(dot + 1)..]);
        return true;
    }

    /// <summary>Gets whether the text is a valid plugin ID.</summary>
    public static bool IsValid([NotNullWhen(true)] string? text) => TryParse(text, out _);

    public override string ToString() => Publisher is null ? "" : $"{Publisher}.{Name}";
}
