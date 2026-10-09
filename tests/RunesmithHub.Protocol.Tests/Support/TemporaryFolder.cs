namespace RunesmithHub.Protocol.Tests.Support;

/// <summary>A folder that is deleted with everything in it when disposed.</summary>
internal sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rshub-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
