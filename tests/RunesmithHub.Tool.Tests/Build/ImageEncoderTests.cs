using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Tool.Images;
using RunesmithHub.Tool.Tests.Support;
using SkiaSharp;

namespace RunesmithHub.Tool.Tests.Build;

public sealed class ImageEncoderTests : IDisposable
{
    private static readonly byte[] AnimatedGif =
    [
        .. "GIF89a"u8, 1, 0, 1, 0, 0x80, 0, 0, 0, 0, 0, 255, 255, 255,
        0x21, 0xF9, 4, 0, 10, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0,
        0x21, 0xF9, 4, 0, 10, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0,
        0x3B,
    ];

    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string Image(string name, int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png, string text = "metadata")
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var data = bitmap.Encode(format, 90);
        File.WriteAllBytes(_folder.Combine(name), [.. data.ToArray(), .. System.Text.Encoding.UTF8.GetBytes(text)]);
        return name;
    }

    private static DeveloperManifest Manifest(string icon, string? banner = null, params string[] gallery) => new()
    {
        Icon = icon,
        Banner = banner,
        Gallery = [.. gallery.Select((path, index) => new ManifestGalleryImage { Path = path, Caption = $"Screen {index + 1}" })],
    };

    [Fact]
    public void ReEncodesEveryImageAtTheHubsSizes()
    {
        var manifest = Manifest(Image("icon.png", 512, 512), Image("banner.jpg", 2000, 1000, SKEncodedImageFormat.Jpeg), Image("shot.webp", 1600, 1000, SKEncodedImageFormat.Webp));

        var images = ImageEncoder.Encode(_folder.Path, manifest);

        Assert.Equal(
            ["icon-32.png", "icon-64.png", "icon-128.png", "icon-256.png", "banner-1280.webp", "banner-640.webp", "gallery-1-1280.webp", "gallery-1-640.webp"],
            images.Select(image => image.File));
        Assert.All(images, image =>
        {
            using var codec = SKCodec.Create(SKData.CreateCopy(image.Content.Span));
            Assert.Equal((image.Width, image.Height), (codec.Info.Width, codec.Info.Height));
            Assert.Equal(image.Format == "png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Webp, codec.EncodedFormat);
        });
        Assert.Equal((640, 400), (images[^1].Width, images[^1].Height));
        Assert.Equal("Screen 1", images[^1].Caption);
    }

    [Fact]
    public void GivesTheSameBytesEveryTime()
    {
        var manifest = Manifest(Image("icon.png", 300, 300));

        Assert.Equal(ImageEncoder.Encode(_folder.Path, manifest).Select(image => image.Content.ToArray()), ImageEncoder.Encode(_folder.Path, manifest).Select(image => image.Content.ToArray()));
    }

    [Theory]
    [InlineData(256, 300, "must be square")]
    [InlineData(128, 128, "must be 256 to 1024 wide")]
    [InlineData(2048, 2048, "must be 256 to 1024 wide")]
    public void RefusesIconsOfTheWrongShapeOrSize(int width, int height, string problem)
    {
        var exception = Assert.Throws<ToolException>(() => ImageEncoder.Encode(_folder.Path, Manifest(Image("icon.png", width, height))));

        Assert.Contains(problem, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesABannerThatIsNotTwoToOne()
    {
        var exception = Assert.Throws<ToolException>(() => ImageEncoder.Encode(_folder.Path, Manifest(Image("icon.png", 256, 256), Image("banner.png", 1280, 800))));

        Assert.Contains("must be 2:1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesSvgJpegIconsAndAnimatedImages()
    {
        File.WriteAllText(_folder.Combine("icon.png"), "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");
        File.WriteAllBytes(_folder.Combine("shot.png"), AnimatedGif);

        var exception = Assert.Throws<ToolException>(() => ImageEncoder.Encode(_folder.Path, Manifest("icon.png", Image("banner.jpg", 1280, 640, SKEncodedImageFormat.Jpeg), "shot.png")));

        Assert.Contains("The icon icon.png is not a PNG, WebP or JPEG image.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("The gallery image shot.png is animated", exception.Message, StringComparison.Ordinal);
        var jpegIcon = Assert.Throws<ToolException>(() => ImageEncoder.Encode(_folder.Path, Manifest(Image("icon.jpg", 256, 256, SKEncodedImageFormat.Jpeg))));
        Assert.Contains("it must be Png or Webp", jpegIcon.Message, StringComparison.Ordinal);
    }
}
