namespace RunesmithHub.Protocol.Updating;

/// <summary>
/// Keeps the trusted index in a folder. Saving writes a complete new copy beside the current one and then swaps the two folders, so a
/// crash part way leaves either the old or the new copy.
/// </summary>
public sealed class FileTrustStore : ITrustStore
{
    private const string CurrentFolder = "current";
    private const string StagingPrefix = ".staging-";
    private const string RetiredPrefix = ".retired-";
    private const string RootFile = "root.json";
    private const string TimestampFile = "timestamp.json";
    private const string SnapshotFile = "snapshot.json";
    private const string ModerationFile = "moderation.json";
    private const string PublishersFile = "publishers.json";
    private const string PolicyFile = "policy.json";
    private const string PluginsFolder = "plugins";

    private readonly string _directory;

    public FileTrustStore(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _directory = directory;
    }

    private string Current => Path.Combine(_directory, CurrentFolder);

    public TrustedIndex? Load()
    {
        RecoverInterruptedSave();
        var current = Current;
        var root = Path.Combine(current, RootFile);
        if (!File.Exists(root))
            return null;

        return new TrustedIndex(File.ReadAllBytes(root), LoadFiles(current));
    }

    private static TrustedFiles? LoadFiles(string folder)
    {
        string[] names = [TimestampFile, SnapshotFile, ModerationFile, PublishersFile, PolicyFile];
        if (!names.All(name => File.Exists(Path.Combine(folder, name))))
            return null;

        var plugins = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        var pluginsFolder = Path.Combine(folder, PluginsFolder);
        if (Directory.Exists(pluginsFolder))
        {
            foreach (var file in Directory.EnumerateFiles(pluginsFolder, "*.json"))
                plugins[Path.GetFileNameWithoutExtension(file)] = File.ReadAllBytes(file);
        }

        return new TrustedFiles
        {
            Timestamp = File.ReadAllBytes(Path.Combine(folder, TimestampFile)),
            Snapshot = File.ReadAllBytes(Path.Combine(folder, SnapshotFile)),
            Moderation = File.ReadAllBytes(Path.Combine(folder, ModerationFile)),
            Publishers = File.ReadAllBytes(Path.Combine(folder, PublishersFile)),
            Policy = File.ReadAllBytes(Path.Combine(folder, PolicyFile)),
            Plugins = plugins,
        };
    }

    public void Save(TrustedIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        Directory.CreateDirectory(_directory);
        RecoverInterruptedSave();
        foreach (var leftover in Directory.EnumerateDirectories(_directory, ".*").Where(IsLeftover).ToList())
            Directory.Delete(leftover, recursive: true);

        var staging = Path.Combine(_directory, StagingPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            WriteFolder(staging, index);
            var current = Current;
            if (Directory.Exists(current))
            {
                var retired = Path.Combine(_directory, RetiredPrefix + Guid.NewGuid().ToString("N"));
                Directory.Move(current, retired);
                Directory.Move(staging, current);
                Directory.Delete(retired, recursive: true);
            }
            else
            {
                Directory.Move(staging, current);
            }
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static bool IsLeftover(string folder)
    {
        var name = Path.GetFileName(folder);
        return name.StartsWith(StagingPrefix, StringComparison.Ordinal) || name.StartsWith(RetiredPrefix, StringComparison.Ordinal);
    }

    private static void WriteFolder(string folder, TrustedIndex index)
    {
        Directory.CreateDirectory(folder);
        Write(Path.Combine(folder, RootFile), index.Root);
        if (index.Files is not { } files)
            return;

        Write(Path.Combine(folder, TimestampFile), files.Timestamp);
        Write(Path.Combine(folder, SnapshotFile), files.Snapshot);
        Write(Path.Combine(folder, ModerationFile), files.Moderation);
        Write(Path.Combine(folder, PublishersFile), files.Publishers);
        Write(Path.Combine(folder, PolicyFile), files.Policy);
        var plugins = Directory.CreateDirectory(Path.Combine(folder, PluginsFolder)).FullName;
        foreach (var (id, bytes) in files.Plugins)
        {
            if (!PluginId.IsValid(id))
                throw new ArgumentException($"\"{id}\" is not a plugin ID.", nameof(index));
            Write(Path.Combine(plugins, id + ".json"), bytes);
        }
    }

    private static void Write(string path, ReadOnlyMemory<byte> bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes.Span);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>Finishes or undoes a save that stopped between moving the old copy away and moving the new one in.</summary>
    private void RecoverInterruptedSave()
    {
        if (!Directory.Exists(_directory) || Directory.Exists(Current))
            return;

        var retired = Directory.EnumerateDirectories(_directory, RetiredPrefix + "*").FirstOrDefault();
        if (retired is not null)
            Directory.Move(retired, Current);
    }
}
