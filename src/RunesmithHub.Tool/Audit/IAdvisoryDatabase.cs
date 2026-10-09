namespace RunesmithHub.Tool.Audit;

/// <summary>A NuGet package at one version.</summary>
internal sealed record PackageVersion(string Name, string Version);

/// <summary>A security advisory; <paramref name="Severity"/> is the database's label, such as <c>CRITICAL</c>, when it has one.</summary>
internal sealed record Advisory(string Id, string Summary, string? Severity)
{
    public bool IsCritical => string.Equals(Severity, "CRITICAL", StringComparison.OrdinalIgnoreCase);

    public string Url => $"https://osv.dev/vulnerability/{Uri.EscapeDataString(Id)}";
}

/// <summary>Looks up known vulnerabilities of NuGet packages.</summary>
internal interface IAdvisoryDatabase
{
    /// <summary>Gets the advisories that affect each package version; versions without any are left out.</summary>
    Task<IReadOnlyDictionary<PackageVersion, IReadOnlyList<Advisory>>> FindAsync(IReadOnlyCollection<PackageVersion> packages, CancellationToken cancellationToken);
}
