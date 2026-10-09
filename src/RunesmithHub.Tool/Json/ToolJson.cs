using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace RunesmithHub.Tool.Json;

/// <summary>Writes the tool's own informational files as compact JSON, escaping only what JSON requires.</summary>
internal static class ToolJson
{
    private static readonly ToolJsonContext Context = new(new JsonSerializerOptions(ToolJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = null,
    });

    public static byte[] Serialize<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, (JsonTypeInfo<T>)Context.GetTypeInfo(typeof(T))!);
}
