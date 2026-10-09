namespace RunesmithHub.Tool.Registry;

/// <summary>The registry as a folder on disk.</summary>
internal sealed class DirectoryRegistryFiles(string root) : IRegistryFiles
{
    public string Root { get; } = Path.GetFullPath(root);

    public async Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var file = Path.Combine(Root, path);
        return File.Exists(file) ? await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false) : null;
    }

    public Task<IReadOnlyList<string>> ListAsync(string directory, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Root, directory);
        IReadOnlyList<string> files = Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(Root, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)]
            : [];
        return Task.FromResult(files);
    }
}
