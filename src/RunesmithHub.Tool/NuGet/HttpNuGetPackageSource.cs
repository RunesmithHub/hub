using System.Net;
using System.Text.Json;

namespace RunesmithHub.Tool.NuGet;

/// <summary>
/// Downloads packages from the flat container of the feed the source mapping assigns: nuget.org for third-party packages, the hub feed for
/// <c>Runesmith.*</c> and <c>RunesmithHub.*</c>.
/// </summary>
internal sealed class HttpNuGetPackageSource(HttpClient http, Uri hubFeed) : INuGetPackageSource
{
    private const long MaxPackageBytes = 256L * 1024 * 1024;
    private const string PackageBaseAddress = "PackageBaseAddress/3.0.0";

    private Uri? _hubFlatContainer;

    public async Task<byte[]> GetAsync(string id, string version, CancellationToken cancellationToken = default)
    {
        var container = NuGetFeeds.IsHubPackage(id) ? await HubFlatContainerAsync(cancellationToken).ConfigureAwait(false) : new Uri(NuGetFeeds.NuGetOrgFlatContainer);
        var lowerId = id.ToLowerInvariant();
        var lowerVersion = version.ToLowerInvariant();
        var url = new Uri(container, $"{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg");
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new ToolException($"{id} {version} is not on {container.Host}.");
            response.EnsureSuccessStatusCode();
            return await ReadLimitedAsync(response.Content, url, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ToolException($"Could not download {id} {version} from {url}: {exception.Message}", exception);
        }
    }

    private async Task<Uri> HubFlatContainerAsync(CancellationToken cancellationToken)
    {
        if (_hubFlatContainer is not null)
            return _hubFlatContainer;
        try
        {
            var index = await http.GetByteArrayAsync(hubFeed, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(index);
            var address = document.RootElement.GetProperty("resources").EnumerateArray()
                .Where(resource => resource.GetProperty("@type").GetString() == PackageBaseAddress)
                .Select(resource => resource.GetProperty("@id").GetString())
                .FirstOrDefault() ?? throw new ToolException($"The hub feed {hubFeed} has no {PackageBaseAddress} resource.");
            return _hubFlatContainer = new Uri(address.EndsWith('/') ? address : address + "/");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or UriFormatException)
        {
            throw new ToolException($"Could not read the hub feed's service index {hubFeed}: {exception.Message}", exception);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, Uri url, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxPackageBytes)
            throw new ToolException($"{url} is larger than {MaxPackageBytes} bytes.");
        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxPackageBytes)
                throw new ToolException($"{url} is larger than {MaxPackageBytes} bytes.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
