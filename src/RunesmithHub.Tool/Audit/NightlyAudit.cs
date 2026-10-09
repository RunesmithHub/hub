using System.Globalization;
using System.Text.Json;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Audit;

/// <summary>A problem the audit found, which becomes an issue titled <c>&lt;subject&gt;: &lt;kind&gt;</c> unless one is open already.</summary>
internal sealed record AuditFinding(string Subject, string Kind, string Body)
{
    public string Title => $"{Subject}: {Kind}";

    /// <summary>Reads a finding's subject and kind back from an issue title.</summary>
    public static (string Subject, string Kind)? ParseTitle(string title)
    {
        var colon = title.IndexOf(": ", StringComparison.Ordinal);
        return colon <= 0 ? null : (title[..colon], title[(colon + 2)..]);
    }
}

/// <summary>The nightly audit: each finding opens one issue, and plugins whose repository moved or vanished are frozen through a pull request.</summary>
internal sealed class NightlyAudit(IGitHubReader github, IGitHubWriter bot, IDownloader downloads, IAdvisoryDatabase advisories, TimeProvider time)
{
    private const long SbomLimit = 16 * 1024 * 1024;
    private static readonly TimeSpan Warning = TimeSpan.FromDays(30);

    public async Task<IReadOnlyList<AuditFinding>> RunAsync(RegistryState registry, TrustRoot trust, IndexState? deployed, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();
        var freezes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var registration in registry.Registrations.Where(registration => registration.State is not (RegistrationState.Deleted or RegistrationState.Blocked)))
        {
            var publisher = registry.FindPublisher(PluginId.Parse(registration.Id).Publisher);
            if (publisher is null)
                continue;
            var repository = await github.GetRepositoryAsync(registration.Repository.Id, cancellationToken).ConfigureAwait(false);
            var identity = Identity(registration, publisher, repository);
            if (identity is not null)
            {
                findings.Add(new AuditFinding(registration.Id, "Repository", identity));
                if (registration.State != RegistrationState.Frozen)
                    freezes[RegistryPaths.Registration(registration.Id)] = RegistryJson.Serialize(registration with { State = RegistrationState.Frozen });
                continue;
            }

            if (registration.Tier is PluginTier.Verified or PluginTier.Official)
            {
                var checks = await new VerifiedRequirements(github, time).CheckAsync(publisher, repository!, cancellationToken).ConfigureAwait(false);
                if (checks.Any(check => check.Met == false))
                    findings.Add(new AuditFinding(registration.Id, "Verified requirement", $"{registration.Id} no longer meets a verification requirement.\n\n{VerifiedRequirements.Checklist(checks)}\n"));
            }
        }

        findings.AddRange(await AdvisoriesAsync(registry, deployed, cancellationToken).ConfigureAwait(false));
        findings.AddRange(Expiry(trust, deployed));

        var open = (await github.GetIssuesAsync(HubLocations.Registry, HubLabels.Audit, IssueFilter.Open, cancellationToken).ConfigureAwait(false))
            .Select(issue => issue.Title).ToHashSet(StringComparer.Ordinal);
        foreach (var finding in findings.Where(finding => !open.Contains(finding.Title)))
        {
            var mention = $"\n@{HubLocations.Organization}/{HubLocations.AdminsTeam}\n";
            await bot.CreateIssueAsync(HubLocations.Registry, finding.Title, finding.Body + mention, [HubLabels.Audit], cancellationToken).ConfigureAwait(false);
        }

        const string freezeTitle = "Freeze plugins whose repository changed owner or disappeared";
        var pending = (await github.GetOpenPullRequestsAsync(HubLocations.Registry, cancellationToken).ConfigureAwait(false)).Any(pull => pull.Title == freezeTitle);
        if (freezes.Count > 0 && !pending)
        {
            var body = "The nightly audit found plugins whose repository was deleted, made private or moved to another owner. Freezing them until an " +
                "administrator reviews each one.\n\n" + string.Join('\n', freezes.Keys.Select(path => $"- `{path}`")) + "\n";
            await bot.OpenPullRequestAsync(HubLocations.Registry, new ChangeSet(
                string.Create(CultureInfo.InvariantCulture, $"audit/freeze-{time.GetUtcNow():yyyyMMdd}"), freezeTitle,
                body, freezes, [HubLabels.Audit, HubLabels.Moderation], AutoMerge: true), cancellationToken).ConfigureAwait(false);
        }

        return findings;
    }

    private static string? Identity(Registration registration, PublisherFile publisher, GitHubRepository? repository) => repository switch
    {
        null => $"The repository of {registration.Id} ({registration.Repository.Url}, ID {registration.Repository.Id}) no longer exists or is private. The plugin is frozen until an administrator reviews it.\n",
        { Private: true } => $"The repository of {registration.Id} is private now. The plugin is frozen until an administrator reviews it.\n",
        _ when repository.Owner.Id != publisher.Owner.Id =>
            $"The repository of {registration.Id} now belongs to {repository.Owner.Login} (ID {repository.Owner.Id}), not to the publisher's owner {publisher.Owner.Login}. The plugin is frozen until an administrator reviews it.\n",
        _ => null,
    };

    private async Task<IEnumerable<AuditFinding>> AdvisoriesAsync(RegistryState registry, IndexState? deployed, CancellationToken cancellationToken)
    {
        var published = new List<(Registration Registration, RegistryVersionRecord Record, IReadOnlyList<PackageVersion> Packages)>();
        foreach (var registration in registry.Registrations)
        {
            var indexRecord = deployed?.PluginRecord(registration.Id)?.Record;
            foreach (var record in registry.VersionsOf(registration.Id))
            {
                if (indexRecord?.FindVersion(record.Version) is not { State: VersionState.Published } || record.Sbom is not { } sbom || sbom.Urls.Count == 0)
                    continue;
                var bytes = await downloads.GetAsync(new Uri(sbom.Urls[0]), SbomLimit, cancellationToken).ConfigureAwait(false);
                if (bytes is not null && Hashes.Matches(bytes, sbom.Sha256))
                    published.Add((registration, record, NuGetComponents(bytes)));
            }
        }

        var found = await advisories.FindAsync([.. published.SelectMany(version => version.Packages).Distinct()], cancellationToken).ConfigureAwait(false);
        var findings = new List<AuditFinding>();
        foreach (var (registration, record, packages) in published)
        {
            foreach (var package in packages)
            {
                foreach (var advisory in found.GetValueOrDefault(package) ?? [])
                {
                    var target = $"{registration.Id}@{record.Version}";
                    var proposal = advisory.IsCritical
                        ? $"\nThe advisory is critical. If it is being exploited, an administrator can freeze the version with `/freeze {target} vulnerability`.\n"
                        : "";
                    findings.Add(new AuditFinding($"{registration.Id} {record.Version}", $"Vulnerability {advisory.Id}",
                        $"{package.Name} {package.Version}, bundled in {registration.Id} {record.Version}, is affected by [{advisory.Id}]({advisory.Url}): {advisory.Summary}\n\n" +
                        $"Maintainers: {string.Join(", ", registration.Maintainers.Select(maintainer => "@" + maintainer.Login))}, please release a version with a fixed package.\n" + proposal));
                }
            }
        }

        return findings;
    }

    private IEnumerable<AuditFinding> Expiry(TrustRoot trust, IndexState? deployed)
    {
        var now = time.GetUtcNow();
        var expiring = new List<(string What, DateTimeOffset Expires, string Fix)>
        {
            ("Root", trust.Root.Expires, "Hold a key ceremony to sign the next root."),
        };
        if (deployed?.Snapshot is { } snapshot)
            expiring.Add(("Snapshot", snapshot.Payload.Expires, "Run the publish workflow and approve it; it re-signs the snapshot."));
        if (deployed?.Moderation is { } moderation)
            expiring.Add(("Moderation list", moderation.Payload.Expires, "Run the moderate workflow and approve it; it re-signs the moderation list."));

        return expiring
            .Where(item => item.Expires - now <= Warning)
            .Select(item => new AuditFinding("Index", $"{item.What} expires {item.Expires.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", $"{item.Fix}\n"));
    }

    /// <summary>Reads the NuGet packages from a CycloneDX SBOM's <c>pkg:nuget/</c> package URLs.</summary>
    public static IReadOnlyList<PackageVersion> NuGetComponents(byte[] sbom)
    {
        try
        {
            using var document = JsonDocument.Parse(sbom);
            if (!document.RootElement.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Array)
                return [];
            var packages = new List<PackageVersion>();
            foreach (var component in components.EnumerateArray())
            {
                if (!component.TryGetProperty("purl", out var purl) || purl.GetString() is not { } text || !text.StartsWith("pkg:nuget/", StringComparison.Ordinal))
                    continue;
                var coordinates = text["pkg:nuget/".Length..].Split('?', '#')[0];
                var at = coordinates.LastIndexOf('@');
                if (at > 0)
                    packages.Add(new PackageVersion(Uri.UnescapeDataString(coordinates[..at]), Uri.UnescapeDataString(coordinates[(at + 1)..])));
            }

            return packages;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
