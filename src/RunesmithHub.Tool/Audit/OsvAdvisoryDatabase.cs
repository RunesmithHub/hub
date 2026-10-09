using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RunesmithHub.Tool.Audit;

/// <summary>Queries the OSV database: one batch query for every package, then the details of each advisory found.</summary>
internal sealed class OsvAdvisoryDatabase(HttpClient http) : IAdvisoryDatabase
{
    private static readonly Uri Api = new("https://api.osv.dev/v1/");
    private const int BatchSize = 1000;

    public async Task<IReadOnlyDictionary<PackageVersion, IReadOnlyList<Advisory>>> FindAsync(IReadOnlyCollection<PackageVersion> packages, CancellationToken cancellationToken)
    {
        var found = new Dictionary<PackageVersion, IReadOnlyList<Advisory>>();
        var details = new Dictionary<string, Advisory>(StringComparer.Ordinal);
        foreach (var batch in packages.Distinct().Chunk(BatchSize))
        {
            var queries = new JsonArray([.. batch.Select(package => (JsonNode)new JsonObject
            {
                ["package"] = new JsonObject { ["name"] = package.Name, ["ecosystem"] = "NuGet" },
                ["version"] = package.Version,
            })]);
            using var response = await PostAsync("querybatch", new JsonObject { ["queries"] = queries }, cancellationToken).ConfigureAwait(false);
            var results = response.RootElement.GetProperty("results");
            for (var i = 0; i < batch.Length; i++)
            {
                if (!results[i].TryGetProperty("vulns", out var vulns) || vulns.GetArrayLength() == 0)
                    continue;
                var advisories = new List<Advisory>();
                foreach (var vuln in vulns.EnumerateArray())
                {
                    var id = vuln.GetProperty("id").GetString()!;
                    if (!details.TryGetValue(id, out var advisory))
                        details[id] = advisory = await DetailsAsync(id, cancellationToken).ConfigureAwait(false);
                    advisories.Add(advisory);
                }

                found[batch[i]] = advisories;
            }
        }

        return found;
    }

    private async Task<Advisory> DetailsAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(Api, "vulns/" + Uri.EscapeDataString(id)), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var summary = root.TryGetProperty("summary", out var text) ? text.GetString() ?? id : id;
        var severity = root.TryGetProperty("database_specific", out var specific) && specific.TryGetProperty("severity", out var label) ? label.GetString() : null;
        return new Advisory(id, summary, severity);
    }

    private async Task<JsonDocument> PostAsync(string path, JsonNode body, CancellationToken cancellationToken)
    {
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(new Uri(Api, path), content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
