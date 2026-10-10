using System.Globalization;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Signing;
using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Dashboard;
using RunesmithHub.Tool.Index;
using RunesmithHub.Tool.Json;

namespace RunesmithHub.Tool.Fixture;

/// <summary>What a fixture holds and where its trusted root and keys are.</summary>
internal sealed record FixtureSummary(int Plugins, int Packages, string TrustedRoot, string KeysDirectory);

/// <summary>
/// Writes a complete signed sample index with the layout clients read, the packages and images it links to under <c>files/</c>, and the
/// throwaway keys that signed it under <c>keys/</c>.
/// </summary>
internal sealed class FixtureWriter(TimeProvider time)
{
    private const long ReviewerId = 9001;
    private const int FirstBuildRun = 18_000_000;
    private const int FirstTrackingIssue = 400;
    private static readonly int[] IconSizes = [64, 128, 256];

    public FixtureSummary Write(FixtureOptions options)
    {
        var directory = Path.GetFullPath(options.Directory);
        IndexFolder.EnsureEmpty(directory);
        var now = UtcTimestampConverter.Truncate(time.GetUtcNow());
        var index = new IndexFolder(directory);
        var hosted = new HostedFiles(directory, options.BaseUrl);

        using var keys = new FixtureKeys();
        var keysDirectory = Path.Combine(directory, FixtureKeys.Folder);
        keys.Save(keysDirectory);
        var trustedRoot = index.WriteRoot(1, IndexJson.Serialize(Dsse.Sign(IndexJson.Serialize(Root(keys, now)), keys.Root[0], keys.Root[1])));

        var packages = 0;
        var records = new List<PluginRecord>();
        foreach (var plugin in SampleData.Plugins)
        {
            var record = Record(plugin, hosted, now);
            packages += record.Versions.Count(version => version.Package is not null);
            records.Add(record);
        }

        var pinned = records.ToDictionary(record => record.Id, record => index.WritePinned(IndexPaths.Plugin, IndexJson.Serialize(record), record.Version), StringComparer.Ordinal);
        var publishers = index.WritePinned(IndexPaths.Publishers, IndexJson.Serialize(Publishers()), 1);
        var policy = index.WritePinned(IndexPaths.Policy, IndexJson.Serialize(Policy()), 1);

        var snapshot = Signed(new SnapshotPayload
        {
            Type = PayloadTypes.Snapshot,
            Version = 1,
            Expires = now.AddDays(90),
            Plugins = pinned,
            Publishers = publishers,
            Policy = policy,
        }, keys.Publish);
        var moderation = Signed(new ModerationPayload
        {
            Type = PayloadTypes.Moderation,
            Version = 1,
            Expires = now.AddDays(90),
            Entries = SampleData.Moderation,
        }, keys.Moderation);
        var timestamp = Signed(new TimestampPayload
        {
            Type = PayloadTypes.Timestamp,
            Version = 1,
            Expires = options.Expired ? now.AddHours(-1) : now.AddDays(3),
            Snapshot = index.WritePinned(IndexPaths.Snapshot, snapshot, 1),
            Moderation = index.WritePinned(IndexPaths.Moderation, moderation, 1),
        }, keys.Timestamp);
        index.Write(IndexPaths.Timestamp, timestamp);

        index.Write(IndexPaths.Dashboard, ToolJson.Serialize(Dashboard(records, now)));
        index.Write(IndexPaths.Stats, ToolJson.Serialize(new InstallStats(now, SampleData.Plugins.ToDictionary(p => p.Id, p => p.Installs, StringComparer.Ordinal))));
        return new FixtureSummary(records.Count, packages, trustedRoot, keysDirectory);
    }

    private static byte[] Signed<T>(T payload, ISigner signer)
        where T : SignedPayload => IndexJson.Serialize(Dsse.Sign(IndexJson.Serialize(payload), signer));

    private static RootPayload Root(FixtureKeys keys, DateTimeOffset now)
    {
        static RoleKeys Role(int threshold, params EcdsaSigner[] signers) => new() { KeyIds = [.. signers.Select(s => s.KeyId)], Threshold = threshold };

        return new RootPayload
        {
            Type = PayloadTypes.Root,
            Version = 1,
            Expires = now.AddDays(365),
            Keys = keys.Root.Append(keys.Publish).Append(keys.Moderation).Append(keys.Timestamp).ToDictionary(key => key.KeyId, key => key.PublicKey),
            Roles = new RootRoles
            {
                Root = Role(2, keys.Root),
                Publish = Role(1, keys.Publish),
                Moderation = Role(1, keys.Moderation),
                Timestamp = Role(1, keys.Timestamp),
            },
        };
    }

    private static PluginRecord Record(SamplePlugin plugin, HostedFiles hosted, DateTimeOffset now)
    {
        var publisher = SampleData.Publishers.Single(p => p.Name == plugin.Publisher);
        var icons = IconSizes.ToDictionary(size => size, size => IconRenderer.RenderPng(plugin.Color, plugin.Name[0], size));
        var images = IconSizes.ToDictionary(size => size, size => hosted.WriteImage($"{plugin.Id}/icon-{size}.png", icons[size], size, size));

        var versions = new List<VersionRecord>();
        foreach (var version in plugin.Versions)
        {
            var buildRun = string.Create(CultureInfo.InvariantCulture, $"https://github.com/RunesmithHub/registry/actions/runs/{FirstBuildRun + BuildNumber(plugin, version)}");
            HostedFile? package = null;
            if (version.State is VersionState.Published or VersionState.Yanked)
            {
                var bytes = SamplePackage.Build(plugin, version, publisher.DisplayName, buildRun, icons[64], icons[128]);
                package = hosted.Write($"{plugin.Id}/{version.Version}/{plugin.Id}-{version.Version}.rsplugin", bytes);
            }

            versions.Add(Version(plugin, version, publisher, package, buildRun, now));
        }

        return new PluginRecord
        {
            Type = PayloadTypes.Plugin,
            Version = 1,
            Id = plugin.Id,
            Name = plugin.Name,
            Publisher = plugin.Publisher,
            Tier = plugin.Tier,
            State = plugin.State,
            Repository = new RecordRepository { Id = RepositoryId(plugin), Url = plugin.Repository.Url, Path = plugin.Repository.Path },
            Maintainers = [new GitHubAccount { Id = publisher.OwnerId + 50, Login = $"{publisher.Name}-dev" }],
            Summary = plugin.Summary,
            Categories = plugin.Categories,
            Keywords = plugin.Keywords,
            Deprecated = plugin.Deprecated,
            Icon = new IconSet { Size64 = images[64], Size128 = images[128], Size256 = images[256] },
            Gallery = [],
            Versions = [.. versions.OrderByDescending(version => version.Version)],
        };
    }

    private static VersionRecord Version(SamplePlugin plugin, SampleVersion version, SamplePublisher publisher, HostedFile? package, string buildRun, DateTimeOffset now)
    {
        var reviewed = version.Review == ReviewState.Reviewed;
        var published = version.State is not (VersionState.Review or VersionState.Held);
        return new VersionRecord
        {
            Version = SemanticVersion.Parse(version.Version),
            State = version.State,
            Review = new VersionReview { State = version.Review, By = reviewed ? ReviewerId : null, At = reviewed ? version.Date : null },
            Commit = SamplePackage.Commit(plugin, version),
            Tag = "v" + version.Version,
            ReleaseAuthor = new GitHubAccount { Id = publisher.OwnerId + 50, Login = $"{publisher.Name}-dev" },
            Package = package,
            Contracts = new ContractsPackage
            {
                Assembly = SamplePackage.ContractsFile(plugin),
                Sha256 = Hashes.Sha256(SamplePackage.ContractsAssembly(plugin, version)),
                Package = $"RunesmithHub.{plugin.AssemblyRoot}.Contracts",
                PackageVersion = version.Version,
            },
            RunesmithApi = VersionRange.Parse(SampleData.RunesmithApi).Normalize(),
            Dependencies = [.. plugin.Dependencies.Select(d => new PluginDependency { Id = d.Id, Range = VersionRange.Parse(d.Range).Normalize() })],
            Capabilities = new CapabilityReport
            {
                Declared = [.. plugin.Capabilities.Select(c => new DeclaredCapability { Id = c.Id, Reason = c.Reason })],
                Detected = [.. plugin.Capabilities.Select(c => c.Id)],
            },
            NetworkHosts = plugin.NetworkHosts,
            Platforms = [],
            Analysis = new AnalysisSummary { Verdict = plugin.Tier == PluginTier.Unverified && !reviewed ? AnalysisVerdict.Passed : AnalysisVerdict.Review, Triggers = [] },
            Build = new BuildSummary { Run = buildRun, Sdk = SamplePackage.Sdk, Reproducible = true },
            ReleaseNotes = version.Notes,
            PublishedAt = published ? version.Date : null,
            HoldUntil = version.State == VersionState.Held ? now.AddHours(17) : null,
            Summary = plugin.Summary,
            Name = plugin.Name,
            Categories = plugin.Categories,
            Keywords = plugin.Keywords,
            Deprecated = plugin.Deprecated,
            License = plugin.License,
            ReportUrl = HubLocations.IssueUrl(TrackingIssue(plugin, version)),
            YankReason = version.YankReason,
            Languages = plugin.Languages,
            LanguagePack = plugin.LanguagePack,
        };
    }

    private static int BuildNumber(SamplePlugin plugin, SampleVersion version) =>
        (SampleData.Plugins.ToList().IndexOf(plugin) * 10) + plugin.Versions.ToList().IndexOf(version);

    private static int TrackingIssue(SamplePlugin plugin, SampleVersion version) => FirstTrackingIssue + BuildNumber(plugin, version);

    private static long RepositoryId(SamplePlugin plugin) => 700_000_000 + (SampleData.Plugins.ToList().IndexOf(plugin) * 1000);

    private static PublisherList Publishers() => new()
    {
        Type = PayloadTypes.Publishers,
        Version = 1,
        Publishers = [.. SampleData.Publishers.Select(publisher => new PublisherEntry
        {
            Name = publisher.Name,
            DisplayName = publisher.DisplayName,
            Owner = new PublisherOwner { Id = publisher.OwnerId, Login = publisher.Name, Type = publisher.OwnerType },
            State = PublisherState.Active,
            Verified = publisher.Verified,
            VerifiedAt = publisher.Verified ? new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero) : null,
        })],
    };

    private static PolicyPayload Policy() => new()
    {
        Type = PayloadTypes.Policy,
        Version = 1,
        Categories = Categories.Default,
        Capabilities = Capabilities.DefaultLabels.ToDictionary(pair => pair.Key, pair => new CapabilityLabel { Label = pair.Value }, StringComparer.Ordinal),
        OldestRunesmith = null,
        Mirrors = [],
        HoldHours = 24,
    };

    private static DashboardSummary Dashboard(List<PluginRecord> records, DateTimeOffset now)
    {
        DashboardVersion Entry(PluginRecord plugin, VersionRecord version)
        {
            var sample = SampleData.Plugins.Single(p => p.Id == plugin.Id);
            var sampleVersion = sample.Versions.Single(v => v.Version == version.Version.ToString());
            return new(plugin.Id, version.Version.ToString(), plugin.Tier.ToString().ToLowerInvariant(), sampleVersion.Date, version.HoldUntil, TrackingIssue(sample, sampleVersion));
        }

        var all = records.SelectMany(plugin => plugin.Versions.Select(version => (Plugin: plugin, Version: version))).ToList();
        return new DashboardSummary
        {
            GeneratedAt = now,
            ReviewQueue = [.. all.Where(pair => pair.Version.State == VersionState.Review).Select(pair => Entry(pair.Plugin, pair.Version)).OrderBy(entry => entry.Since)],
            Held = [.. all.Where(pair => pair.Version.State == VersionState.Held).Select(pair => Entry(pair.Plugin, pair.Version))],
            Rejected = [.. SampleData.Rejected.Select(r => new DashboardRejection(r.Plugin, r.Version, r.Reason))],
            Actions = [.. SampleData.Moderation.OrderByDescending(entry => entry.CreatedAt).Select(entry => new DashboardAction(
                entry.Id, entry.Action.ToString().ToLowerInvariant(), entry.Target.ToString(), ReasonText(entry.Reason), entry.CreatedAt))],
            AuditFlags = [.. SampleData.AuditFlags.Select(flag => new DashboardAuditFlag(flag.Plugin, flag.Kind, flag.Note))],
            Reports = [.. SampleData.Reports.Select(report => new DashboardReports(report.Plugin, report.Open))],
        };
    }

    private static string ReasonText(ModerationReason reason) => reason switch
    {
        ModerationReason.CompromisedAccount => "compromised-account",
        ModerationReason.DeveloperRequest => "developer-request",
        _ => reason.ToString().ToLowerInvariant(),
    };
}
