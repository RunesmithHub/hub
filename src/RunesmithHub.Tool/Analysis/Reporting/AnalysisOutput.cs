using RunesmithHub.Protocol.Signing;
using RunesmithHub.Tool.Analysis.Heuristics;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Findings;
using RunesmithHub.Tool.Json;

namespace RunesmithHub.Tool.Analysis.Reporting;

/// <summary>
/// Writes an analysis's files: the public <c>analysis.json</c>, <c>summary.md</c> and SBOM, and, when heuristics ran, the private findings,
/// sealed when there is an administrator key to seal them to.
/// </summary>
internal static class AnalysisOutput
{
    public static async Task WriteAsync(AnalysisResult result, string directory, PublicKey? findingsRecipient, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        var output = Directory.CreateDirectory(directory).FullName;
        await File.WriteAllBytesAsync(Path.Combine(output, AnalysisReport.FileName), ArtifactJson.Serialize(result.Report), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(output, SummaryWriter.FileName), SummaryWriter.Write(result.Report), cancellationToken).ConfigureAwait(false);
        if (result.Sbom is { } sbom)
            await File.WriteAllBytesAsync(Path.Combine(output, CycloneDxSbom.FileName), sbom, cancellationToken).ConfigureAwait(false);
        if (result.HeuristicMatches is not { } matches)
            return;

        var findings = ArtifactJson.Serialize(new PrivateFindings(matches));
        if (findingsRecipient is null)
            await File.WriteAllBytesAsync(Path.Combine(output, PrivateFindings.FileName), findings, cancellationToken).ConfigureAwait(false);
        else
            await File.WriteAllBytesAsync(Path.Combine(output, SealedFindings.FileName), ArtifactJson.Serialize(FindingsCipher.Seal(findings, findingsRecipient)), cancellationToken).ConfigureAwait(false);
    }
}
