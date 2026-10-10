namespace RunesmithHub.Tool.Tests.Support;

/// <summary>A folder that is deleted with everything in it when disposed.</summary>
internal sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rshub-tool-tests-" + Guid.NewGuid().ToString("N"));

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        if (!Directory.Exists(Path))
            return;

        // Git writes its object files read-only, and Windows will not delete read-only files.
        foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Path, recursive: true);
    }
}
