using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Tool.Fixture;

/// <summary>Writes the files the index links to (packages and images) under <c>files/</c>, and gives their URLs.</summary>
internal sealed class HostedFiles(string directory, Uri baseUrl)
{
    private const string Folder = "files";

    /// <summary>Writes a file and describes it for a version record.</summary>
    public HostedFile Write(string relativePath, byte[] bytes) => new()
    {
        Urls = [WriteFile(relativePath, bytes)],
        Sha256 = Hashes.Sha256(bytes),
        Length = bytes.Length,
    };

    /// <summary>Writes a PNG image and describes it for a plugin record.</summary>
    public HostedImage WriteImage(string relativePath, byte[] png, int width, int height) => new()
    {
        Url = WriteFile(relativePath, png),
        Sha256 = Hashes.Sha256(png),
        Length = png.Length,
        Width = width,
        Height = height,
        Format = ImageFormat.Png,
    };

    private string WriteFile(string relativePath, byte[] bytes)
    {
        var path = Path.Combine(directory, Folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return new Uri(baseUrl, $"{Folder}/{relativePath}").ToString();
    }
}
