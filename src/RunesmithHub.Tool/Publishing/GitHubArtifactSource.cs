using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using RunesmithHub.Tool.GitHub;

namespace RunesmithHub.Tool.Publishing;

/// <summary>
/// Downloads artifacts of the registry's workflow runs through the REST API. Only runs of the discover or build workflow on the default
/// branch count, so a pull request's workflow run can never stand in for the pipeline.
/// </summary>
internal sealed class GitHubArtifactSource(GitHubRestClient client) : IArtifactSource
{
    private static readonly string[] PipelineWorkflows = [".github/workflows/discover.yml", ".github/workflows/build.yml"];
    private static readonly string[] PipelineEvents = ["schedule", "workflow_dispatch", "issues"];

    public async Task DownloadAsync(long runId, string name, string directory, CancellationToken cancellationToken)
    {
        await EnsurePipelineRunAsync(runId, cancellationToken).ConfigureAwait(false);
        var path = Invariant($"repos/{HubLocations.Registry}/actions/runs/{runId}/artifacts?name={Uri.EscapeDataString(name)}");
        var list = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        var artifact = list?.GetProperty("artifacts").EnumerateArray().FirstOrDefault(item => !item.GetProperty("expired").GetBoolean());
        if (artifact is not { ValueKind: JsonValueKind.Object } found)
            throw new ToolException(Invariant($"Run {runId} has no artifact named {name}; it may have expired, so the version must be built again."));

        var id = found.GetProperty("id").GetInt64();
        var zip = await client.GetBytesAsync(Invariant($"repos/{HubLocations.Registry}/actions/artifacts/{id}/zip"), "application/vnd.github+json", cancellationToken).ConfigureAwait(false)
            ?? throw new ToolException(Invariant($"The artifact {name} of run {runId} could not be downloaded."));
        Directory.CreateDirectory(directory);
        using var stream = new MemoryStream(zip);
        await ZipFile.ExtractToDirectoryAsync(stream, directory, overwriteFiles: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsurePipelineRunAsync(long runId, CancellationToken cancellationToken)
    {
        var run = await client.GetAsync(Invariant($"repos/{HubLocations.Registry}/actions/runs/{runId}"), cancellationToken).ConfigureAwait(false)
            ?? throw new ToolException(Invariant($"Run {runId} does not exist."));
        var workflow = run.GetProperty("path").GetString();
        var branch = run.GetProperty("head_branch").GetString();
        var trigger = run.GetProperty("event").GetString();
        var defaultBranch = run.GetProperty("repository").TryGetProperty("default_branch", out var named) ? named.GetString() : "main";
        if (!PipelineWorkflows.Contains(workflow) || !PipelineEvents.Contains(trigger) || !string.Equals(branch, defaultBranch, StringComparison.Ordinal))
            throw new ToolException(Invariant($"Run {runId} is not a pipeline run on the default branch ({workflow}, {trigger}, {branch})."));
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
