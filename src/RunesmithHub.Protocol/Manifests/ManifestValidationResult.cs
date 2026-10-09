using System.Diagnostics.CodeAnalysis;

namespace RunesmithHub.Protocol.Manifests;

/// <summary>A manifest as read, and every problem found; <see cref="Manifest"/> is null when the JSON itself could not be read.</summary>
public sealed record ManifestValidationResult(DeveloperManifest? Manifest, IReadOnlyList<ManifestProblem> Problems)
{
    [MemberNotNullWhen(true, nameof(Manifest))]
    public bool IsValid => Manifest is not null && Problems.Count == 0;
}
