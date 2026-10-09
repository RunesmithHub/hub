using System.Net.Http.Headers;
using System.Text.Json;

namespace RunesmithHub.Tool.Analysis.Vulnerabilities;

/// <summary>Asks the OSV database, which includes the GitHub Advisory Database, through its batch API, by package URL.</summary>
internal sealed class OsvDatabase(HttpClient http) : IVulnerabilityDatabase
{
    public static readonly Uri BatchEndpoint = new("https://api.osv.dev/v1/querybatch");

    private const int BatchSize = 500;

    public async Task<IReadOnlyList<PackageAdvisories>> QueryAsync(IReadOnlyList<SbomComponent> packages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var affected = new List<PackageAdvisories>();
        foreach (var batch in packages.Chunk(BatchSize))
        {
            using var content = new ByteArrayContent(Request(batch));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            try
            {
                using var response = await http.PostAsync(BatchEndpoint, content, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                affected.AddRange(Parse(batch, body));
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new ToolException($"Could not ask the OSV vulnerability database: {exception.Message}", exception);
            }
        }

        return affected;
    }

    private static byte[] Request(SbomComponent[] packages)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartArray("queries");
            foreach (var package in packages)
            {
                json.WriteStartObject();
                json.WriteStartObject("package");
                json.WriteString("purl", package.Purl);
                json.WriteEndObject();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static List<PackageAdvisories> Parse(SbomComponent[] packages, byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        var results = document.RootElement.GetProperty("results").EnumerateArray().ToList();
        if (results.Count != packages.Length)
            throw new InvalidOperationException($"OSV answered {results.Count} queries out of {packages.Length}.");

        var affected = new List<PackageAdvisories>();
        for (var i = 0; i < results.Count; i++)
        {
            if (!results[i].TryGetProperty("vulns", out var vulnerabilities))
                continue;
            var ids = vulnerabilities.EnumerateArray().Select(vulnerability => vulnerability.GetProperty("id").GetString()!).Order(StringComparer.Ordinal).ToList();
            if (ids.Count > 0)
                affected.Add(new PackageAdvisories(packages[i].Id, packages[i].Version, ids));
        }

        return affected;
    }
}
