using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Tests.Support;

/// <summary>Builds index records for tests with sensible defaults.</summary>
internal static class Records
{
    public static readonly DateTimeOffset Time = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public static HostedImage Image(int size) => new()
    {
        Url = $"https://example.invalid/icon-{size}.png",
        Sha256 = new string('a', 64),
        Length = 100,
        Width = size,
        Height = size,
        Format = ImageFormat.Png,
    };

    public static VersionRecord Version(
        string version, VersionState state = VersionState.Published, ReviewState review = ReviewState.Reviewed, string api = ">=0.1.0 <0.2.0",
        IEnumerable<(string Id, string Range)>? dependencies = null, IReadOnlyList<string>? platforms = null) => new()
        {
            Version = SemanticVersion.Parse(version),
            State = state,
            Review = new VersionReview { State = review, By = review == ReviewState.Reviewed ? 1 : null, At = review == ReviewState.Reviewed ? Time : null },
            Commit = new string('c', 40),
            Tag = "v" + version,
            ReleaseAuthor = new GitHubAccount { Id = 7, Login = "dev" },
            Package = new HostedFile { Urls = ["https://example.invalid/p.rsplugin"], Sha256 = new string('b', 64), Length = 1000 },
            Contracts = new ContractsPackage { Assembly = "X.Contracts.dll", Sha256 = new string('d', 64), Package = "RunesmithHub.X.Contracts", PackageVersion = version },
            RunesmithApi = VersionRange.Parse(api).Normalize(),
            Dependencies = [.. (dependencies ?? []).Select(d => new PluginDependency { Id = d.Id, Range = VersionRange.Parse(d.Range).Normalize() })],
            Capabilities = new CapabilityReport { Declared = [], Detected = [] },
            NetworkHosts = [],
            Platforms = platforms ?? [],
            Analysis = new AnalysisSummary { Verdict = AnalysisVerdict.Passed, Triggers = [] },
            Build = new BuildSummary { Run = "https://example.invalid/run/1", Sdk = "10.0.400", Reproducible = true },
            ReleaseNotes = "",
            PublishedAt = state == VersionState.Published ? Time : null,
            Summary = "A plugin.",
            Name = "Plugin",
            Categories = ["Other"],
            Keywords = [],
        };

    public static PluginRecord Plugin(
        string id, string name, PluginTier tier, IEnumerable<VersionRecord> versions, PluginState state = PluginState.Listed, int recordVersion = 1) => new()
        {
            Type = PayloadTypes.Plugin,
            Version = recordVersion,
            Id = id,
            Name = name,
            Publisher = PluginId.Parse(id).Publisher,
            Tier = tier,
            State = state,
            Repository = new RecordRepository { Id = 100, Url = $"https://github.com/{id.Replace('.', '/')}", Path = "." },
            Maintainers = [new GitHubAccount { Id = 7, Login = "dev" }],
            Summary = "A plugin.",
            Categories = ["Other"],
            Keywords = [],
            Icon = new IconSet { Size64 = Image(64), Size128 = Image(128), Size256 = Image(256) },
            Gallery = [],
            Versions = [.. versions.OrderByDescending(v => v.Version)],
        };

    public static PublisherEntry Publisher(string name, PublisherState state = PublisherState.Active) => new()
    {
        Name = name,
        DisplayName = name,
        Owner = new PublisherOwner { Id = 1, Login = name, Type = OwnerType.Organization },
        State = state,
        Verified = false,
    };

    public static PublisherList Publishers(IEnumerable<PluginRecord> plugins, params PublisherEntry[] overrides) => new()
    {
        Type = PayloadTypes.Publishers,
        Version = 1,
        Publishers = [.. plugins.Select(p => p.Publisher).Distinct().Select(name => overrides.FirstOrDefault(o => o.Name == name) ?? Publisher(name))],
    };

    public static ModerationEntry Moderation(int id, ModerationTarget target, ModerationAction action, ModerationReason reason = ModerationReason.Malicious) => new()
    {
        Id = id,
        Target = target,
        Action = action,
        Reason = reason,
        Note = "Note.",
        CreatedAt = Time,
    };

    public static HubCatalog Catalog(IEnumerable<PluginRecord> plugins, IReadOnlyList<ModerationEntry>? moderation = null, params PublisherEntry[] publishers)
    {
        var list = plugins.ToList();
        return new HubCatalog(list, Publishers(list, publishers), moderation ?? []);
    }
}
