using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace RunesmithHub.Tool.Json;

/// <summary>Reads and writes the files builds and analyses produce: indented UTF-8 JSON with LF line endings, escaping only what JSON requires.</summary>
internal static class ArtifactJson
{
    private static readonly ArtifactJsonContext Context = new(new JsonSerializerOptions(ArtifactJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = null,
    });

    public static byte[] Serialize<T>(T value) => Encoding.UTF8.GetBytes(SerializeToString(value));

    public static string SerializeToString<T>(T value) => JsonSerializer.Serialize(value, TypeInfo<T>()) + "\n";

    /// <exception cref="InvalidDataException">The JSON is malformed or misses a required field.</exception>
    public static T Deserialize<T>(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, TypeInfo<T>()) ?? throw new InvalidDataException($"Not a valid {typeof(T).Name}: the JSON is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Not a valid {typeof(T).Name}: {exception.Message}", exception);
        }
    }

    private static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)Context.GetTypeInfo(typeof(T))!;
}
