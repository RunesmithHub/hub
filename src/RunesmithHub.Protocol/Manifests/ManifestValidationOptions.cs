namespace RunesmithHub.Protocol.Manifests;

/// <summary>What a manifest is checked against beyond its own rules.</summary>
public sealed record ManifestValidationOptions
{
    public static ManifestValidationOptions Default { get; } = new();

    /// <summary>Gets the categories the policy allows.</summary>
    public IReadOnlyList<string> Categories { get; init; } = Protocol.Categories.Default;

    /// <summary>Gets whether an SPDX license ID may be used.</summary>
    public Func<string, bool> IsApprovedLicense { get; init; } = OsiLicenses.IsApproved;

    /// <summary>Gets the plugin ID the registration holds, when known.</summary>
    public string? ExpectedId { get; init; }

    /// <summary>Gets the repository URL the registration holds, when known.</summary>
    public string? ExpectedRepository { get; init; }

    /// <summary>Gets the version the release tag names, when known.</summary>
    public string? ExpectedVersion { get; init; }
}
