namespace RunesmithHub.Tool.NuGet;

/// <summary>Gets the bytes of a package file (<c>.nupkg</c>) by ID and version.</summary>
internal interface INuGetPackageSource
{
    /// <exception cref="ToolException">The package could not be found or downloaded.</exception>
    Task<byte[]> GetAsync(string id, string version, CancellationToken cancellationToken = default);
}
