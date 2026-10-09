using System.Text.Json;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Tool.Json;

namespace RunesmithHub.Tool.IndexBuild;

/// <summary>
/// A version the publish job has just released: written by <c>publish artifacts</c> and read by <c>index publish</c>, which marks it published,
/// and by <c>publish announce</c>. Later index builds carry these facts forward from the signed index they verified.
/// </summary>
internal sealed record Publication
{
    public const string FileName = "publications.json";

    public required string Plugin { get; init; }

    public required string Version { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    /// <summary>Gets the review as published: the approving administrator for reviewed versions.</summary>
    public required VersionReview Review { get; init; }

    public required int TrackingIssue { get; init; }

    /// <summary>Gets the URL of the release in the packages repository.</summary>
    public required string Release { get; init; }

    public string Key => $"{Plugin}@{Version}";

    public static IReadOnlyList<Publication> Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllBytes(path), ToolJsonContext.Default.IReadOnlyListPublication) ?? [];
        }
        catch (JsonException exception)
        {
            throw new ToolException($"{path}: {exception.Message}", exception);
        }
    }

    public static void Write(string path, IReadOnlyList<Publication> publications) => File.WriteAllBytes(path, ToolJson.Serialize(publications));
}
