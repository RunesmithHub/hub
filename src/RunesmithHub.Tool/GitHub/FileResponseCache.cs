using System.Text.Json;
using RunesmithHub.Tool.Json;

namespace RunesmithHub.Tool.GitHub;

/// <summary>A response cache stored in one JSON file, which a workflow keeps between runs.</summary>
internal sealed class FileResponseCache : IResponseCache
{
    private readonly string _path;
    private readonly Dictionary<string, CachedResponse> _entries;
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public FileResponseCache(string path)
    {
        _path = path;
        _entries = File.Exists(path) ? Read(path) : new Dictionary<string, CachedResponse>(StringComparer.Ordinal);
    }

    public CachedResponse? Get(string url)
    {
        _used.Add(url);
        return _entries.GetValueOrDefault(url);
    }

    public void Set(string url, CachedResponse response)
    {
        _used.Add(url);
        _entries[url] = response;
    }

    /// <summary>Writes the entries used in this run, so the file never grows with resources nobody asks for any more.</summary>
    public void Save()
    {
        var kept = _entries.Where(pair => _used.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllBytes(_path, ToolJson.Serialize<IReadOnlyDictionary<string, CachedResponse>>(kept));
    }

    private static Dictionary<string, CachedResponse> Read(string path)
    {
        try
        {
            var entries = JsonSerializer.Deserialize(File.ReadAllBytes(path), ToolJsonContext.Default.IReadOnlyDictionaryStringCachedResponse);
            return entries is null ? new(StringComparer.Ordinal) : new(entries, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new(StringComparer.Ordinal);
        }
    }
}
