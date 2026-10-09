using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Vulnerabilities;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>A vulnerability database that knows the advisories it is given and records what it was asked.</summary>
internal sealed class FakeVulnerabilities : IVulnerabilityDatabase
{
    public Dictionary<string, IReadOnlyList<string>> Advisories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Asked { get; } = [];

    public Task<IReadOnlyList<PackageAdvisories>> QueryAsync(IReadOnlyList<SbomComponent> packages, CancellationToken cancellationToken = default)
    {
        Asked.AddRange(packages.Select(package => package.Purl));
        IReadOnlyList<PackageAdvisories> affected =
        [
            .. packages.Where(package => Advisories.ContainsKey(package.Purl)).Select(package => new PackageAdvisories(package.Id, package.Version, Advisories[package.Purl])),
        ];
        return Task.FromResult(affected);
    }
}
