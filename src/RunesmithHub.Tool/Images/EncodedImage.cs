using System.Text.Json.Serialization;

namespace RunesmithHub.Tool.Images;

/// <summary>A re-encoded image the hub hosts, at the file name it is written under.</summary>
/// <param name="Kind"><c>icon</c>, <c>banner</c> or <c>gallery</c>.</param>
/// <param name="Index">The gallery position, starting at 1; null for the icon and banner.</param>
/// <param name="Format"><c>png</c> or <c>webp</c>.</param>
internal sealed record EncodedImage(string Kind, int? Index, string? Caption, int Width, int Height, string Format, string File)
{
    [JsonIgnore]
    public ReadOnlyMemory<byte> Content { get; init; }
}

/// <summary><c>images.json</c>: the re-encoded images of a build with their sizes and gallery captions, for the plugin record.</summary>
internal sealed record ImageList(IReadOnlyList<EncodedImage> Images)
{
    public const string FileName = "images.json";
}
