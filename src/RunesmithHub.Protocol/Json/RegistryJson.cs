using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace RunesmithHub.Protocol.Json;

/// <summary>
/// Reads and writes human-edited files: developer manifests and registry records, as two-space indented JSON with LF line endings, rejecting
/// unknown properties.
/// </summary>
public static class RegistryJson
{
    /// <summary>Serializes to indented UTF-8 JSON without a byte order mark, ending in a newline.</summary>
    /// <exception cref="NotSupportedException">The type is not a registry type.</exception>
    public static byte[] Serialize<T>(T value)
        where T : class => Encoding.UTF8.GetBytes(SerializeToString(value));

    /// <inheritdoc cref="Serialize{T}(T)"/>
    public static string SerializeToString<T>(T value)
        where T : class => JsonSerializer.Serialize(value, TypeInfo<T>()) + "\n";

    /// <summary>Deserializes UTF-8 JSON strictly.</summary>
    /// <exception cref="JsonException">The JSON is malformed, has an unknown property or misses a required field; the exception has the path.</exception>
    public static T Deserialize<T>(ReadOnlySpan<byte> json)
        where T : class => JsonSerializer.Deserialize(json, TypeInfo<T>()) ?? throw new JsonException("The JSON is null.");

    private static JsonTypeInfo<T> TypeInfo<T>() =>
        JsonContexts.Registry.GetTypeInfo(typeof(T)) as JsonTypeInfo<T> ?? throw new NotSupportedException($"{typeof(T).Name} is not a registry type.");
}
