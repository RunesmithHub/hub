using SkiaSharp;

namespace RunesmithHub.Tool.Images;

/// <summary>What one kind of developer image must be before the hub re-encodes it.</summary>
internal sealed record ImageRules(
    string Kind,
    IReadOnlyList<SKEncodedImageFormat> Formats,
    long MaxBytes,
    int MinWidth,
    int MaxWidth,
    int MaxHeight,
    double? AspectRatio)
{
    private const long Megabyte = 1024 * 1024;

    public static ImageRules Icon { get; } = new("icon", [SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp], Megabyte, 256, 1024, 1024, 1.0);

    public static ImageRules Banner { get; } =
        new("banner", [SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp, SKEncodedImageFormat.Jpeg], 2 * Megabyte, 1280, 2560, 1280, 2.0);

    public static ImageRules Gallery { get; } =
        new("gallery image", [SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp, SKEncodedImageFormat.Jpeg], 2 * Megabyte, 1, 2560, 1600, null);

    /// <summary>Checks a decoded image's format, size and shape, returning the problem or null.</summary>
    public string? Check(string path, long length, SKEncodedImageFormat format, int width, int height)
    {
        if (length > MaxBytes)
            return $"The {Kind} {path} is {length} bytes; at most {MaxBytes} are allowed.";
        if (!Formats.Contains(format))
            return $"The {Kind} {path} is {format}; it must be {string.Join(" or ", Formats)}.";
        if (AspectRatio is { } ratio && Math.Abs((width / (double)height) - ratio) > 0.0001)
            return $"The {Kind} {path} is {width} by {height} pixels; it must be {(ratio == 1.0 ? "square" : $"{ratio}:1")}.";
        if (width < MinWidth || width > MaxWidth || height > MaxHeight)
            return $"The {Kind} {path} is {width} by {height} pixels; it must be {MinWidth} to {MaxWidth} wide and at most {MaxHeight} high.";
        return null;
    }
}
