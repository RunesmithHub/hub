using System.Globalization;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Gates;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Registry;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Publishing;

/// <summary>Releases merged versions the deployed index has not published yet, skipping whatever already exists with the right hash.</summary>
internal sealed class ArtifactPublisher(IGitHubReader github, IGitHubWriter bot, IArtifactSource artifacts, IFeedPublisher feed, TimeProvider time, string? botLogin)
{
    public async Task<(IReadOnlyList<Publication> Publications, IReadOnlyList<string> Notes)> PublishAsync(
        RegistryState registry, IndexState deployed, string workDirectory, CancellationToken cancellationToken)
    {
        var publications = new List<Publication>();
        var notes = new List<string>();
        foreach (var (registration, record) in Pending(registry, deployed))
        {
            var folder = Path.Combine(workDirectory, registration.Id, record.Version.ToString());
            await artifacts.DownloadAsync(record.BuildRun, WorkflowArtifacts.Package(registration.Id, record.Version), folder, cancellationToken).ConfigureAwait(false);
            var output = BuildOutput.Open(folder, registration.Id, record.Version, registry.LimitsOf(registration), ExpectedHashes.FromRecord(RecordedHashes(record)));
            var mismatches = Mismatches(record, output).ToList();
            if (mismatches.Count > 0)
                throw new ToolException($"{registration.Id} {record.Version}: the build output does not match the record: {string.Join("; ", mismatches)}.");

            var release = await bot.EnsureReleaseAsync(HubLocations.Packages, HubLocations.ReleaseTag(registration.Id, record.Version), $"{record.Name} {record.Version}",
                $"{registration.Id} {record.Version}, built from {registration.Repository.Url}/commit/{record.Commit} by {record.Build.Run}.\n", cancellationToken).ConfigureAwait(false);
            var uploaded = 0;
            foreach (var file in output.ReleaseFiles)
            {
                if (await UploadAsync(release, file, cancellationToken).ConfigureAwait(false))
                    uploaded++;
            }

            var pushed = await feed.PushAsync(output.ContractsPackage.Path, record.Contracts.Package, record.Contracts.PackageVersion, record.Assets!.ContractsPackage.Sha256, cancellationToken).ConfigureAwait(false);
            var review = record.State == VersionState.Review ? await ReviewAsync(registration.Id, record, cancellationToken).ConfigureAwait(false) : record.Review;
            publications.Add(new Publication
            {
                Plugin = registration.Id,
                Version = record.Version.ToString(),
                PublishedAt = UtcTimestampConverter.Truncate(time.GetUtcNow()),
                Review = review,
                TrackingIssue = record.TrackingIssue,
                Release = release.HtmlUrl,
            });
            notes.Add(string.Create(CultureInfo.InvariantCulture,
                $"{registration.Id} {record.Version}: {uploaded} files uploaded, contracts package {(pushed == FeedPushResult.Pushed ? "pushed" : "already on the feed")}."));
        }

        if (publications.Count == 0)
            notes.Add("No version is waiting to be published.");
        return (publications, notes);
    }

    /// <summary>Lists the merged versions the index has not published, leaving out what is restricted or moderated.</summary>
    public static IEnumerable<(Registration Registration, RegistryVersionRecord Record)> Pending(RegistryState registry, IndexState deployed) =>
        from registration in registry.Registrations.OrderBy(registration => registration.Id, StringComparer.Ordinal)
        where registration.State is not (RegistrationState.Blocked or RegistrationState.Deleted or RegistrationState.Frozen)
        let publisher = PluginId.Parse(registration.Id).Publisher
        where registry.FindPublisher(publisher)?.State == PublisherState.Active
        from record in registry.VersionsOf(registration.Id).Reverse()
        where record.State is VersionState.Review or VersionState.Held or VersionState.Published
        where !registry.ActiveModeration.Any(entry => entry.Target.Covers(publisher, registration.Id, record.Version))
        where deployed.PluginRecord(registration.Id)?.Record.FindVersion(record.Version)?.PublishedAt is null
        select (registration, record);

    private static IEnumerable<string?> RecordedHashes(RegistryVersionRecord record)
    {
        IEnumerable<string?> files = [record.Package?.Sha256, record.Source?.Sha256, record.Sbom?.Sha256];
        if (record.Assets is not { } assets)
            return files;
        IEnumerable<HostedImage> images = [assets.Icon.Size64, assets.Icon.Size128, assets.Icon.Size256, .. assets.Banner is { } banner ? [banner.Width640, banner.Width1280] : Array.Empty<HostedImage>(),
            .. assets.Gallery.SelectMany(item => new[] { item.Width640, item.Width1280 })];
        return [.. files, assets.ContractsPackage.Sha256, .. images.Select(image => image.Sha256)];
    }

    private static IEnumerable<string> Mismatches(RegistryVersionRecord record, BuildOutput output)
    {
        if (record.Assets is not { } assets)
        {
            yield return "the record has no assets";
            yield break;
        }

        var expected = new List<(string What, OutputFile File, string Sha256, long Length)>
        {
            ("package", output.Package, record.Package!.Sha256, record.Package.Length),
            ("source archive", output.Source, record.Source!.Sha256, record.Source.Length),
            ("SBOM", output.Sbom, record.Sbom!.Sha256, record.Sbom.Length),
            ("contracts package", output.ContractsPackage, assets.ContractsPackage.Sha256, assets.ContractsPackage.Length),
            ("64 pixel icon", output.Icons[0].File, assets.Icon.Size64.Sha256, assets.Icon.Size64.Length),
            ("128 pixel icon", output.Icons[1].File, assets.Icon.Size128.Sha256, assets.Icon.Size128.Length),
            ("256 pixel icon", output.Icons[2].File, assets.Icon.Size256.Sha256, assets.Icon.Size256.Length),
        };
        if (output.Banner is { } banner && assets.Banner is { } recorded)
        {
            expected.Add(("640 pixel banner", banner.Width640.File, recorded.Width640.Sha256, recorded.Width640.Length));
            expected.Add(("1280 pixel banner", banner.Width1280.File, recorded.Width1280.Sha256, recorded.Width1280.Length));
        }
        else if (output.Banner is not null || assets.Banner is not null)
        {
            yield return "the banner";
        }

        if (output.Gallery.Count != assets.Gallery.Count)
            yield return "the number of gallery images";
        foreach (var (built, item) in output.Gallery.Zip(assets.Gallery))
        {
            expected.Add(("gallery image", built.Width640.File, item.Width640.Sha256, item.Width640.Length));
            expected.Add(("gallery image", built.Width1280.File, item.Width1280.Sha256, item.Width1280.Length));
        }

        foreach (var (what, file, sha256, length) in expected)
        {
            if (!string.Equals(file.Sha256, sha256, StringComparison.Ordinal) || file.Length != length)
                yield return $"the {what} ({file.Name})";
        }
    }

    private async Task<bool> UploadAsync(ReleaseWithAssets release, OutputFile file, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(file.Name);
        if (release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, name, StringComparison.Ordinal)) is { } existing)
        {
            return existing.Size == file.Length && (existing.Sha256 is null || string.Equals(existing.Sha256, file.Sha256, StringComparison.Ordinal))
                ? false
                : throw new ToolException($"The release {release.HtmlUrl} already has a different {name}.");
        }

        await bot.UploadAssetAsync(release, name, await File.ReadAllBytesAsync(file.Path, cancellationToken).ConfigureAwait(false), ContentType(name), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Finds the administrator who approved a reviewed version's pull request.</summary>
    private async Task<VersionReview> ReviewAsync(string pluginId, RegistryVersionRecord record, CancellationToken cancellationToken)
    {
        var pull = await github.FindPullRequestAsync(HubLocations.Registry, TrackingIssues.Branch(pluginId, record.Version), cancellationToken).ConfigureAwait(false);
        if (pull is not { Merged: true })
            throw new ToolException($"{pluginId} {record.Version} needs review, but its version pull request was not merged.");
        var admins = await github.GetTeamMemberIdsAsync(HubLocations.Organization, HubLocations.AdminsTeam, cancellationToken).ConfigureAwait(false);
        var reviews = await github.GetReviewsAsync(HubLocations.Registry, pull.Number, cancellationToken).ConfigureAwait(false);
        var comments = await github.GetCommentsAsync(HubLocations.Registry, pull.Number, cancellationToken).ConfigureAwait(false);
        var approval = Approvals.Of(reviews, comments, pull.HeadSha, botLogin).Where(approval => admins.Contains(approval.By.Id)).MinBy(approval => approval.At)
            ?? throw new ToolException($"{pluginId} {record.Version} needs review, but no administrator approved #{pull.Number}.");
        return new VersionReview { State = ReviewState.Reviewed, By = approval.By.Id, At = UtcTimestampConverter.Truncate(approval.At) };
    }

    private static string ContentType(string name) => Path.GetExtension(name) switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".json" => "application/json",
        ".gz" => "application/gzip",
        _ => "application/zip",
    };
}
