using RunesmithHub.Protocol.Index;

namespace RunesmithHub.Tool.Fixture;

/// <summary>A sample publisher.</summary>
internal sealed record SamplePublisher(string Name, string DisplayName, long OwnerId, OwnerType OwnerType, bool Verified);

/// <summary>A sample plugin version.</summary>
internal sealed record SampleVersion(string Version, VersionState State, ReviewState Review, DateTimeOffset Date, string Notes = "")
{
    public string? YankReason { get; init; }
}

/// <summary>A sample capability with the reason users see.</summary>
internal sealed record SampleCapability(string Id, string Reason);

/// <summary>A sample plugin; every version has the same dependencies, capabilities and languages.</summary>
internal sealed record SamplePlugin(
    string Id, string Name, string AssemblyRoot, PluginTier Tier, string Color, IReadOnlyList<string> Categories, string Summary, string License,
    IReadOnlyList<SampleVersion> Versions)
{
    public PluginState State { get; init; } = PluginState.Listed;

    public IReadOnlyList<SampleCapability> Capabilities { get; init; } = [];

    public IReadOnlyList<string> NetworkHosts { get; init; } = [];

    public IReadOnlyList<(string Id, string Range)> Dependencies { get; init; } = [];

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public Deprecation? Deprecated { get; init; }

    public IReadOnlyList<string>? Languages { get; init; }

    public LanguagePackSummary? LanguagePack { get; init; }

    /// <summary>Gets the repository URL and the plugin's folder in it.</summary>
    public (string Url, string Path) Repository { get; init; }

    public int Installs { get; init; }

    public string Publisher => Id[..Id.IndexOf('.', StringComparison.Ordinal)];
}

/// <summary>The sample hub: the plugins, publishers and moderation of the design's examples, on Runesmith API 0.1.</summary>
internal static class SampleData
{
    public const string RunesmithApi = "^0.1.0";

    private static DateTimeOffset Day(int month, int day) => new(2026, month, day, 9, 0, 0, TimeSpan.Zero);

    private static SampleVersion Reviewed(string version, int month, int day, string notes = "") => new(version, VersionState.Published, ReviewState.Reviewed, Day(month, day), notes);

    private static SampleVersion Automated(string version, int month, int day, string notes = "") => new(version, VersionState.Published, ReviewState.Automated, Day(month, day), notes);

    private static (string, string) Runesmith(string folder) => ("https://github.com/CodeByDylan/Runesmith", $"plugins/{folder}");

    private static (string, string) Community(string owner, string repository) => ($"https://github.com/{owner}/{repository}", ".");

    public static IReadOnlyList<SamplePublisher> Publishers { get; } =
    [
        new("runesmith", "Runesmith", 1001, OwnerType.Organization, true),
        new("harbor", "Harbor Collective", 2001, OwnerType.Organization, true),
        new("northwind", "Northwind Tools", 2002, OwnerType.Organization, true),
        new("ember", "Ember Studio", 2003, OwnerType.Organization, true),
        new("tide", "Tide Labs", 2004, OwnerType.Organization, true),
        new("lumen", "Lumen Labs", 3001, OwnerType.Organization, false),
        new("atlas", "Atlas Dev", 3002, OwnerType.User, false),
        new("cobalt", "cobalt-oss", 3003, OwnerType.Organization, false),
        new("pixel", "pixelkit", 3004, OwnerType.User, false),
        new("copper", "Copper", 3005, OwnerType.User, false),
        new("fastcode", "fastcode", 3006, OwnerType.User, false),
    ];

    public static IReadOnlyList<SamplePlugin> Plugins { get; } =
    [
        new("runesmith.csharp", "C#", "Runesmith.CSharp", PluginTier.Official, "#5b4fe0", ["Languages"],
            "C# editing with completion, diagnostics, navigation, refactorings and the .NET build.", "Apache-2.0",
            [Reviewed("0.4.6", 10, 2), Reviewed("0.4.5", 9, 18)])
        {
            Capabilities = [new("process", "Runs the .NET SDK to build, test and restore projects."), new("network", "Downloads .NET SDKs when you ask it to.")],
            NetworkHosts = ["builds.dotnet.microsoft.com"],
            Keywords = ["csharp", "dotnet"],
            Repository = Runesmith("Runesmith.CSharp"),
            Installs = 48210,
        },
        new("runesmith.git", "Git", "Runesmith.Git", PluginTier.Official, "#5b4fe0", ["Version control"],
            "Commit, branch, fetch, history and diffs for Git repositories.", "Apache-2.0",
            [Reviewed("0.3.4", 9, 28), Reviewed("0.3.3", 9, 1), Reviewed("0.3.0", 7, 14), Reviewed("0.2.9", 6, 30)])
        {
            Capabilities = [new("process", "Runs git for operations the library does not cover.")],
            Keywords = ["git", "vcs"],
            Languages = ["en", "nl", "de"],
            Repository = Runesmith("Runesmith.Git"),
            Installs = 51930,
        },
        new("runesmith.github", "GitHub", "Runesmith.GitHub", PluginTier.Official, "#5b4fe0", ["Version control", "Cloud and remote"],
            "Sign in with GitHub, clone your repositories and add organizations.", "Apache-2.0",
            [Reviewed("0.2.1", 10, 4)])
        {
            Capabilities = [new("network", "Talks to GitHub's API to list and clone your repositories."), new("credentials", "Keeps your GitHub sign-in in your system's secure storage.")],
            NetworkHosts = ["github.com", "api.github.com"],
            Dependencies = [("runesmith.git", "^0.3.0")],
            Repository = Runesmith("Runesmith.GitHub"),
            Installs = 30110,
        },
        new("runesmith.dutch", "Dutch Language Pack", "Runesmith.Dutch", PluginTier.Official, "#5b4fe0", ["Other"],
            "Runesmith and its GitHub plugin in Dutch.", "Apache-2.0",
            [Reviewed("0.1.0", 10, 3)])
        {
            Keywords = ["dutch", "nederlands"],
            LanguagePack = new LanguagePackSummary { Language = "nl", Name = "Nederlands", EnglishName = "Dutch", Targets = ["runesmith", "runesmith.github"] },
            Repository = Runesmith("Runesmith.Dutch"),
            Installs = 6120,
        },
        new("harbor.issues", "Harbor Issues", "Harbor.Issues", PluginTier.Verified, "#0f766e", ["Productivity", "Version control"],
            "Links commits, branches and TODOs to issues in GitHub, Forgejo and Gitea.", "Apache-2.0",
            [
                new("1.4.0", VersionState.Review, ReviewState.Automated, Day(10, 8), "Adds Gitea support."),
                Reviewed("1.3.2", 9, 30, "Fixes the token refresh for Forgejo."),
                new("1.3.1", VersionState.Blocked, ReviewState.Reviewed, Day(9, 27), "Blocked: depended on a compromised version of a logging library."),
                Reviewed("1.3.0", 9, 2),
                Reviewed("1.2.0", 8, 10),
            ])
        {
            Capabilities = [new("network", "Reads issues from your issue tracker."), new("credentials", "Keeps the access token for your issue tracker.")],
            NetworkHosts = ["api.github.com", "codeberg.org"],
            Dependencies = [("runesmith.git", "^0.3.0")],
            Keywords = ["issues", "github", "forgejo"],
            Repository = Community("harbor-collective", "harbor-issues"),
            Installs = 8420,
        },
        new("northwind.charts", "Northwind Charts", "Northwind.Charts", PluginTier.Verified, "#b45309", ["Visualization", "Data and databases"],
            "Preview CSV and JSON files as charts beside the editor.", "MIT",
            [Reviewed("2.1.0", 9, 12), Reviewed("2.0.0", 8, 1), Reviewed("1.9.4", 6, 11)])
        {
            Keywords = ["charts", "csv"],
            Repository = Community("northwind-tools", "charts"),
            Installs = 5120,
        },
        new("ember.themes", "Ember Themes", "Ember.Themes", PluginTier.Verified, "#be123c", ["Themes"],
            "Six warm dark themes and two light ones, tuned for long sessions.", "MIT",
            [Reviewed("3.0.1", 8, 22)])
        {
            Keywords = ["themes", "dark"],
            Repository = Community("ember-studio", "ember-themes"),
            Installs = 12700,
        },
        new("tide.formatter", "Tide Formatter", "Tide.Formatter", PluginTier.Verified, "#1d4ed8", ["Formatters and linters"],
            "Formats on save with the formatter your project already uses.", "MIT",
            [Reviewed("1.8.0", 9, 25)])
        {
            Capabilities = [new("process", "Runs your project's formatter, such as dotnet format.")],
            Repository = Community("tide-labs", "tide-formatter"),
            Installs = 9340,
        },
        new("lumen.todo", "Lumen Todo", "Lumen.Todo", PluginTier.Unverified, "#7c3aed", ["Productivity"],
            "Collects TODO comments across your project and links them to issues.", "MIT",
            [
                new("1.1.0", VersionState.Held, ReviewState.Automated, Day(10, 9), "Groups TODOs by author."),
                Automated("1.0.0", 10, 6, "First stable release."),
                new("0.9.2", VersionState.Yanked, ReviewState.Automated, Day(9, 20), "Withdrawn: crashed on empty files.") { YankReason = "Crashed on empty files." },
            ])
        {
            Dependencies = [("harbor.issues", ">=1.2.0"), ("runesmith.git", "^0.3.1")],
            Keywords = ["todo"],
            Repository = Community("lumen-labs", "lumen-todo"),
            Installs = 2210,
        },
        new("atlas.rest", "Atlas REST Client", "Atlas.Rest", PluginTier.Unverified, "#0369a1", ["Cloud and remote", "Testing"],
            "Send HTTP requests from .http files and see responses side by side.", "GPL-3.0-only",
            [Automated("0.6.0", 9, 29)])
        {
            Capabilities = [new("network", "Sends the HTTP requests you write to the servers you name."), new("filesystem", "Saves response bodies where you choose.")],
            NetworkHosts = ["*"],
            Keywords = ["http", "rest"],
            Repository = Community("atlas-dev", "atlas-rest"),
            Installs = 3460,
        },
        new("cobalt.docker", "Cobalt Docker", "Cobalt.Docker", PluginTier.Unverified, "#2563eb", ["Build and run", "Cloud and remote"],
            "Build, run and inspect containers from the editor.", "MIT",
            [Automated("0.3.2", 9, 8)])
        {
            Capabilities = [new("process", "Runs the docker command line."), new("environment", "Reads DOCKER_HOST to find your container engine.")],
            Keywords = ["docker", "containers"],
            Repository = Community("cobalt-oss", "cobalt-docker"),
            Installs = 1890,
        },
        new("pixel.icons", "Pixel File Icons", "Pixel.Icons", PluginTier.Unverified, "#db2777", ["Themes"],
            "Crisp pixel-art icons for 400 file types.", "MIT",
            [Automated("2.2.0", 10, 7), Automated("2.1.3", 9, 15)])
        {
            Keywords = ["icons"],
            Repository = Community("pixelkit", "pixel-icons"),
            Installs = 4020,
        },
        new("copper.snippets", "Copper Snippets", "Copper.Snippets", PluginTier.Unverified, "#a16207", ["Snippets and templates"],
            "Snippets for C# and Java.", "MIT",
            [Automated("1.2.0", 5, 2)])
        {
            State = PluginState.Deprecated,
            Deprecated = new Deprecation { Reason = "No longer maintained; the C# plugin has these snippets built in.", Replacement = "runesmith.csharp" },
            Repository = Community("copper", "copper-snippets"),
            Installs = 2750,
        },
        new("fastcode.helper", "FastCode Helper", "FastCode.Helper", PluginTier.Unverified, "#525252", ["Productivity"],
            "Speeds up common editing tasks.", "MIT",
            [new("1.0.1", VersionState.Blocked, ReviewState.Automated, Day(9, 19))])
        {
            State = PluginState.Blocked,
            Repository = Community("fastcode", "helper"),
            Installs = 0,
        },
    ];

    /// <summary>The moderation entries the sample's moderation list carries.</summary>
    public static IReadOnlyList<ModerationEntry> Moderation { get; } =
    [
        new()
        {
            Id = 1,
            Target = new ModerationTarget { Kind = ModerationTargetKind.Publisher, Publisher = "fastcode" },
            Action = ModerationAction.Block,
            Reason = ModerationReason.Malicious,
            Note = "Read browser cookie stores and sent them to a chat webhook.",
            Advisory = "https://github.com/RunesmithHub/registry/security/advisories/RSHUB-2026-0007",
            Remediation = "Remove it.",
            CreatedAt = Day(9, 19),
        },
        new()
        {
            Id = 2,
            Target = new ModerationTarget { Kind = ModerationTargetKind.Versions, Plugin = "harbor.issues", Range = Protocol.Versioning.VersionRange.Parse("1.3.1") },
            Action = ModerationAction.Block,
            Reason = ModerationReason.Vulnerability,
            Note = "Depended on a compromised version of a logging library.",
            Remediation = "Update to 1.3.2.",
            CreatedAt = Day(9, 29),
        },
        new()
        {
            Id = 3,
            Target = new ModerationTarget { Kind = ModerationTargetKind.Plugin, Plugin = "pixel.icons" },
            Action = ModerationAction.Freeze,
            Reason = ModerationReason.Broken,
            Note = "Installs are paused while Runesmith looks into a report.",
            CreatedAt = Day(10, 8),
        },
    ];

    /// <summary>Versions the pipeline rejected recently, for the dashboard.</summary>
    public static IReadOnlyList<(string Plugin, string Version, string Reason)> Rejected { get; } =
    [
        ("atlas.rest", "0.6.1", "Undeclared network use"),
        ("northwind.charts", "2.2.0-beta.1", "Contracts changed without a major version"),
        ("ember.themes", "3.1.0", "Known vulnerability in a bundled package"),
    ];

    /// <summary>Findings of the nightly audit, for the dashboard.</summary>
    public static IReadOnlyList<(string Plugin, string Kind, string Note)> AuditFlags { get; } =
    [
        ("tide.formatter", "Verified requirement", "Default branch protection was turned off."),
        ("cobalt.docker", "Ownership", "Repository transferred to another owner."),
        ("ember.themes", "Vulnerability", "New advisory for a bundled package (low)."),
    ];

    /// <summary>Open public reports per plugin, for the dashboard.</summary>
    public static IReadOnlyList<(string Plugin, int Open)> Reports { get; } = [("pixel.icons", 3), ("atlas.rest", 1), ("cobalt.docker", 1)];
}
