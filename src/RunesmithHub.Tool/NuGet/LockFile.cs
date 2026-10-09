using System.Text.Json;

namespace RunesmithHub.Tool.NuGet;

/// <summary>A package NuGet's lock file pins, with the base64 SHA-512 of the package file it checked.</summary>
internal sealed record LockedPackage(string Id, string Version, string ContentHash);

/// <summary>Reads a project's <c>packages.lock.json</c>.</summary>
internal static class LockFile
{
    public const string FileName = "packages.lock.json";

    /// <summary>Gets the lock file beside a project file.</summary>
    public static string PathFor(string projectFile) => Path.Combine(Path.GetDirectoryName(projectFile)!, FileName);

    /// <summary>Reads every locked package of every target framework once, sorted by ID; project references are left out.</summary>
    /// <exception cref="ToolException">The file is not a lock file.</exception>
    public static IReadOnlyList<LockedPackage> Read(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var packages = new Dictionary<string, LockedPackage>(StringComparer.OrdinalIgnoreCase);
            foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                foreach (var dependency in framework.Value.EnumerateObject())
                {
                    var entry = dependency.Value;
                    if (string.Equals(entry.GetProperty("type").GetString(), "Project", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var package = new LockedPackage(dependency.Name, entry.GetProperty("resolved").GetString()!, entry.GetProperty("contentHash").GetString()!);
                    packages.TryAdd($"{package.Id}/{package.Version}", package);
                }
            }

            return [.. packages.Values.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase).ThenBy(package => package.Version, StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ToolException($"{path} is not a valid NuGet lock file: {exception.Message}", exception);
        }
    }
}
