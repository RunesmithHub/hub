using SkiaSharp;

namespace RunesmithHub.Tool.Fixture;

/// <summary>Draws placeholder plugin icons: a rounded tile in the plugin's color with its initial in white.</summary>
internal static class IconRenderer
{
    /// <summary>Renders a square PNG icon.</summary>
    /// <param name="color">The tile color as <c>#rrggbb</c>.</param>
    public static byte[] RenderPng(string color, char initial, int size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var tile = new SKPaint { Color = SKColor.Parse(color), IsAntialias = true };
        var radius = size * 0.22f;
        canvas.DrawRoundRect(new SKRect(0, 0, size, size), radius, radius, tile);

        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = false };
        var cell = size / 12f;
        var left = (size - (PixelFont.Width * cell)) / 2;
        var top = (size - (PixelFont.Height * cell)) / 2;
        foreach (var (x, y) in PixelFont.Pixels(initial))
            canvas.DrawRect(SKRect.Create(left + (x * cell), top + (y * cell), cell, cell), ink);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
