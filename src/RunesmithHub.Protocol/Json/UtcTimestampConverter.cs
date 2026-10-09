using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Json;

/// <summary>Writes times as ISO 8601 UTC with <c>Z</c> and second precision, such as <c>2026-10-09T12:00:00Z</c>.</summary>
public sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null || !DateTimeOffset.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
            throw new JsonException($"\"{text}\" is not a UTC time such as 2026-10-09T12:00:00Z.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(Truncate(value).ToString(Format, CultureInfo.InvariantCulture));
    }

    /// <summary>Converts a time to UTC and drops everything below a second.</summary>
    public static DateTimeOffset Truncate(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }
}
