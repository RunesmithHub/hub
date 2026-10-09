namespace RunesmithHub.Protocol.Manifests;

/// <summary>One thing wrong with a file, at a field path such as <c>dependencies[0].range</c>; the path is empty for the whole file.</summary>
public sealed record ManifestProblem(string Path, string Message)
{
    public override string ToString() => Path.Length == 0 ? Message : $"{Path}: {Message}";
}
