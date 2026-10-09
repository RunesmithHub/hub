using System.Globalization;
using RunesmithHub.Protocol.Manifests;
using SkiaSharp;

namespace RunesmithHub.Tool.Images;

/// <summary>
/// Decodes the developer's icon, banner and gallery images and re-encodes them to fresh files at the hub's sizes, which drops metadata and
/// anything appended to the originals. SVG and animated images are refused.
/// </summary>
internal static class ImageEncoder
{
    public const string Folder = "images";

    private const int WebpQuality = 90;
    private static readonly int[] IconSizes = [32, 64, 128, 256];
    private static readonly int[] WideSizes = [1280, 640];
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    /// <summary>Re-encodes every image the manifest names, paths relative to the plugin root.</summary>
    /// <exception cref="ToolException">An image breaks the rules; every problem is listed.</exception>
    public static IReadOnlyList<EncodedImage> Encode(string root, DeveloperManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var problems = new List<string>();
        var images = new List<EncodedImage>();

        if (Decode(root, manifest.Icon!, ImageRules.Icon, problems) is { } icon)
        {
            using (icon)
                images.AddRange(IconSizes.Select(size => Encode(icon, size, size, SKEncodedImageFormat.Png, new EncodedImage("icon", null, null, size, size, "png", $"icon-{size}.png"))));
        }

        if (manifest.Banner is { } bannerPath && Decode(root, bannerPath, ImageRules.Banner, problems) is { } banner)
        {
            using (banner)
                images.AddRange(WideSizes.Select(width => Encode(banner, width, width / 2, SKEncodedImageFormat.Webp, new EncodedImage("banner", null, null, width, width / 2, "webp", $"banner-{width}.webp"))));
        }

        var gallery = manifest.Gallery ?? [];
        for (var i = 0; i < gallery.Count; i++)
        {
            if (Decode(root, gallery[i].Path!, ImageRules.Gallery, problems) is not { } image)
                continue;
            using (image)
            {
                foreach (var width in WideSizes)
                {
                    var height = Math.Max(1, (int)Math.Round(image.Height * (width / (double)image.Width), MidpointRounding.AwayFromZero));
                    var file = string.Create(CultureInfo.InvariantCulture, $"gallery-{i + 1}-{width}.webp");
                    images.Add(Encode(image, width, height, SKEncodedImageFormat.Webp, new EncodedImage("gallery", i + 1, gallery[i].Caption, width, height, "webp", file)));
                }
            }
        }

        if (problems.Count > 0)
            throw new ToolException("The images can't be used:\n" + string.Join("\n", problems.Select(problem => $"  {problem}")));
        return images;
    }

    private static SKBitmap? Decode(string root, string relativePath, ImageRules rules, List<string> problems)
    {
        var path = Path.Combine(root, relativePath);
        var bytes = File.ReadAllBytes(path);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            problems.Add($"The {rules.Kind} {relativePath} is not a PNG, WebP or JPEG image.");
            return null;
        }

        if (codec.FrameCount > 1)
        {
            problems.Add($"The {rules.Kind} {relativePath} is animated; only still images are accepted.");
            return null;
        }

        if (rules.Check(relativePath, bytes.Length, codec.EncodedFormat, codec.Info.Width, codec.Info.Height) is { } problem)
        {
            problems.Add(problem);
            return null;
        }

        var bitmap = SKBitmap.Decode(codec, new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (bitmap is null)
            problems.Add($"The {rules.Kind} {relativePath} could not be decoded.");
        return bitmap;
    }

    private static EncodedImage Encode(SKBitmap source, int width, int height, SKEncodedImageFormat format, EncodedImage image)
    {
        using var resized = source.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), Sampling)
            ?? throw new ToolException($"Could not resize {image.File}.");
        using var encoded = resized.Encode(format, format == SKEncodedImageFormat.Png ? 100 : WebpQuality)
            ?? throw new ToolException($"Could not encode {image.File}.");
        return image with { Content = encoded.ToArray() };
    }
}
