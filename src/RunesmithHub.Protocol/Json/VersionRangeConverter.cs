using System.Text.Json;
using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Json;

/// <summary>Reads a <see cref="VersionRange"/> from its text and writes its canonical text.</summary>
public sealed class VersionRangeConverter : JsonConverter<VersionRange>
{
    public override VersionRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        return VersionRange.TryParse(text, out var range, out var error) ? range : throw new JsonException(error);
    }

    public override void Write(Utf8JsonWriter writer, VersionRange value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.ToString());
    }
}
