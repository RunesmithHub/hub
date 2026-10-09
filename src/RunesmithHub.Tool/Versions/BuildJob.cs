using System.Text.Json;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Tool.Json;

namespace RunesmithHub.Tool.Versions;

/// <summary>
/// One release to build, as discovery emits it into the workflow matrix and the build workflow stores it as <c>build-info.json</c> next to
/// the analysis.
/// </summary>
internal sealed record BuildJob
{
    public const string FileName = "build-info.json";

    public required string Plugin { get; init; }

    public required string Version { get; init; }

    public required string Tag { get; init; }

    public required string Commit { get; init; }

    /// <summary>Gets the repository as <c>owner/name</c>, current at discovery.</summary>
    public required string Repository { get; init; }

    public required long RepositoryId { get; init; }

    /// <summary>Gets the plugin's folder in the repository.</summary>
    public required string Path { get; init; }

    public required GitHubAccount ReleaseAuthor { get; init; }

    public required int TrackingIssue { get; init; }

    /// <summary>Gets the release notes as plain text, at most 10,000 characters.</summary>
    public required string ReleaseNotes { get; init; }

    /// <exception cref="ToolException">The file is not a build job.</exception>
    public static BuildJob Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllBytes(path), ToolJsonContext.Default.BuildJob) ?? throw new ToolException($"{path} is empty.");
        }
        catch (JsonException exception)
        {
            throw new ToolException($"{path} is not a build job: {exception.Message}", exception);
        }
    }
}
