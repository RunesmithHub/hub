using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Build;

/// <summary>
/// Writes a CycloneDX 1.6 software bill of materials listing every package the plugin's lock files pin, with its package URL and hash.
/// Bundled packages are <c>required</c> and carry their license; the rest (the SDK, other plugins' contracts, build-only packages) are
/// <c>excluded</c>. Nothing time-dependent is written, so the same build gives the same bytes.
/// </summary>
internal static class CycloneDxSbom
{
    public const string FileName = "sbom.cdx.json";

    /// <summary>Gets the package URL of a NuGet package.</summary>
    public static string PackageUrl(string id, string version) => $"pkg:nuget/{Uri.EscapeDataString(id)}@{Uri.EscapeDataString(version)}";

    public static byte[] Create(string pluginId, string version, string commit, string repository, IReadOnlyList<LockedPackage> locked, IReadOnlyList<BundledPackage> bundled)
    {
        ArgumentNullException.ThrowIfNull(locked);
        ArgumentNullException.ThrowIfNull(bundled);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, IndentSize = 2, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteString("bomFormat", "CycloneDX");
            json.WriteString("specVersion", "1.6");
            json.WriteString("serialNumber", SerialNumber(pluginId, version, commit));
            json.WriteNumber("version", 1);

            json.WriteStartObject("metadata");
            json.WriteStartObject("component");
            json.WriteString("type", "application");
            json.WriteString("bom-ref", $"{pluginId}@{version}");
            json.WriteString("name", pluginId);
            json.WriteString("version", version);
            json.WriteStartArray("externalReferences");
            json.WriteStartObject();
            json.WriteString("type", "vcs");
            json.WriteString("url", $"{repository}/tree/{commit}");
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndObject();

            json.WriteStartArray("components");
            foreach (var package in locked.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase).ThenBy(package => package.Version, StringComparer.Ordinal))
                WriteComponent(json, package, bundled.FirstOrDefault(b => string.Equals(b.Id, package.Id, StringComparison.OrdinalIgnoreCase) && b.Version == package.Version));
            json.WriteEndArray();
            json.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    private static void WriteComponent(Utf8JsonWriter json, LockedPackage package, BundledPackage? bundled)
    {
        var purl = PackageUrl(package.Id, package.Version);
        json.WriteStartObject();
        json.WriteString("type", "library");
        json.WriteString("bom-ref", purl);
        json.WriteString("name", package.Id);
        json.WriteString("version", package.Version);
        json.WriteString("scope", bundled is null ? "excluded" : "required");
        json.WriteString("purl", purl);
        json.WriteStartArray("hashes");
        json.WriteStartObject();
        json.WriteString("alg", "SHA-512");
        json.WriteString("content", Convert.ToHexStringLower(Convert.FromBase64String(package.ContentHash)));
        json.WriteEndObject();
        json.WriteEndArray();
        if (bundled?.License is { } license && (license.Expression ?? license.Url) is not null)
        {
            json.WriteStartArray("licenses");
            json.WriteStartObject();
            if (license.Expression is { } expression)
                json.WriteString("expression", expression);
            else
            {
                json.WriteStartObject("license");
                json.WriteString("name", "See URL");
                json.WriteString("url", license.Url);
                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    private static string SerialNumber(string pluginId, string version, string commit)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{pluginId}@{version}@{commit}"));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return "urn:uuid:" + new Guid(hash.AsSpan(0, 16), bigEndian: true).ToString("D");
    }
}
