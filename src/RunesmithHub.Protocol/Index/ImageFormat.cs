using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Protocol.Index;

/// <summary>The format of a hosted image.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<ImageFormat>))]
public enum ImageFormat
{
    Png,
    Webp,
}
