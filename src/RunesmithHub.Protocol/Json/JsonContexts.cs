using System.Text.Encodings.Web;
using System.Text.Json;

namespace RunesmithHub.Protocol.Json;

/// <summary>The serializer contexts with the escaping every hub implementation shares: only what JSON requires is escaped.</summary>
internal static class JsonContexts
{
    public static IndexJsonContext Index { get; } = new(Relaxed(IndexJsonContext.Default.Options));

    public static RegistryJsonContext Registry { get; } = new(Relaxed(RegistryJsonContext.Default.Options));

    private static JsonSerializerOptions Relaxed(JsonSerializerOptions source) =>
        new(source) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, TypeInfoResolver = null };
}
