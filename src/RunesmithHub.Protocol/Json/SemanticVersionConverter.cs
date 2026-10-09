using System.Text.Json;
using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Json;

/// <summary>Reads and writes a <see cref="SemanticVersion"/> as its text.</summary>
public sealed class SemanticVersionConverter : JsonConverter<SemanticVersion>
{
    public override SemanticVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        return SemanticVersion.TryParse(text, out var version) ? version : throw new JsonException($"\"{text}\" is not a valid semantic version.");
    }

    public override void Write(Utf8JsonWriter writer, SemanticVersion value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.ToString());
    }
}
