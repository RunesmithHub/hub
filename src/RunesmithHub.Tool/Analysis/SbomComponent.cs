using System.Text.Json;

namespace RunesmithHub.Tool.Analysis;

/// <summary>A NuGet package the SBOM lists: bundled into the package, or only used to build it.</summary>
internal sealed record SbomComponent(string Id, string Version, string Purl, bool Bundled, string? License)
{
    /// <summary>Reads the components of a CycloneDX SBOM the hub wrote.</summary>
    /// <exception cref="InvalidDataException">The SBOM is not in the form the hub writes.</exception>
    public static IReadOnlyList<SbomComponent> Read(ReadOnlySpan<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            if (!document.RootElement.TryGetProperty("components", out var components))
                return [];
            return [.. components.EnumerateArray().Select(component => new SbomComponent(
                component.GetProperty("name").GetString()!,
                component.GetProperty("version").GetString()!,
                component.GetProperty("purl").GetString()!,
                component.TryGetProperty("scope", out var scope) && scope.GetString() == "required",
                LicenseOf(component)))];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException($"The SBOM is not valid: {exception.Message}", exception);
        }
    }

    private static string? LicenseOf(JsonElement component)
    {
        if (!component.TryGetProperty("licenses", out var licenses))
            return null;
        foreach (var license in licenses.EnumerateArray())
        {
            if (license.TryGetProperty("expression", out var expression))
                return expression.GetString();
        }

        return null;
    }
}
