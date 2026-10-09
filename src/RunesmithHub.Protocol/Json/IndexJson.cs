using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;

namespace RunesmithHub.Protocol.Json;

/// <summary>
/// Reads and writes signed metadata, index records, envelopes and packaged manifests: compact JSON that ignores unknown properties but
/// rejects a newer <c>specVersion</c> or <c>schemaVersion</c> than this library supports.
/// </summary>
public static class IndexJson
{
    /// <summary>Serializes to compact UTF-8 JSON.</summary>
    /// <exception cref="NotSupportedException">The type is not an index type.</exception>
    public static byte[] Serialize<T>(T value)
        where T : class => JsonSerializer.SerializeToUtf8Bytes(value, TypeInfo<T>());

    /// <summary>Deserializes UTF-8 JSON.</summary>
    /// <exception cref="InvalidDataException">The JSON is malformed, misses a required field, or has a newer format version.</exception>
    public static T Deserialize<T>(ReadOnlySpan<byte> json)
        where T : class
    {
        T? value;
        try
        {
            value = JsonSerializer.Deserialize(json, TypeInfo<T>());
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Not a valid {typeof(T).Name}: {exception.Message}", exception);
        }

        switch (value)
        {
            case null:
                throw new InvalidDataException($"Not a valid {typeof(T).Name}: the JSON is null.");
            case IndexDocument { SpecVersion: > IndexDocument.SupportedSpecVersion } document:
                throw new InvalidDataException($"The {document.Type} file has specVersion {document.SpecVersion}; this version reads up to {IndexDocument.SupportedSpecVersion}.");
            case PackagedManifest { SchemaVersion: > DeveloperManifest.CurrentSchemaVersion } manifest:
                throw new InvalidDataException($"The packaged manifest has schemaVersion {manifest.SchemaVersion}; this version reads up to {DeveloperManifest.CurrentSchemaVersion}.");
            default:
                return value;
        }
    }

    private static JsonTypeInfo<T> TypeInfo<T>() =>
        JsonContexts.Index.GetTypeInfo(typeof(T)) as JsonTypeInfo<T> ?? throw new NotSupportedException($"{typeof(T).Name} is not an index type.");
}
