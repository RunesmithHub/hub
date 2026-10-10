using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Registry;
using RunesmithHub.Tool.Tests.Support;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Tests.Versions;

public sealed class VersionProposerTests : IDisposable
{
    private readonly TestRegistry _registry = new();
    private readonly TemporaryFolder _folder = new();
    private readonly FakeGitHub _github = new();

    public VersionProposerTests() => _registry.Publisher(Samples.Publisher()).Registration(Samples.Registration());

    public void Dispose()
    {
        _folder.Dispose();
        _registry.Dispose();
    }

    private VersionInputs Inputs(string version, IReadOnlyList<string>? triggers = null, GitHubAccount? author = null, string name = "Lumen Todo")
    {
        var output = Samples.WriteBuildOutput(_folder.Combine(version), version, name);
        var verdict = triggers is { Count: > 0 } ? AnalysisOutcome.Review : AnalysisOutcome.Passed;
        return VersionInputs.Read(Samples.Analysis(output, version, verdict, triggers), Samples.Job(version, author: author), output, 18_000_002);
    }

    private async Task<RegistryVersionRecord> AddPublishedAsync(string version, VersionState state = VersionState.Published)
    {
        var record = await Samples.RecordAsync(await _registry.LoadAsync(), Samples.WriteBuildOutput(_folder.Combine("old-" + version), version), version);
        _registry.Version(Samples.PluginId, record with { State = state });
        return record;
    }

    private async Task<RegistryVersionRecord> CreateAsync(VersionInputs inputs, IRegistryHistory? history = null) =>
        (await VersionRecordFactory.CreateAsync(inputs, await _registry.LoadAsync(), history ?? new Samples.NoHistory(), TestContext.Current.CancellationToken)).Record;

    [Fact]
    public async Task ComputesTheRecordFromTheBuildOutputAnalysisAndJob()
    {
        var record = await CreateAsync(Inputs("1.0.0", [ReviewTriggers.FirstVersion]));

        Assert.Equal(VersionState.Review, record.State);
        Assert.Equal([ReviewTriggers.FirstVersion], record.Analysis.Triggers);
        Assert.Equal(AnalysisVerdict.Review, record.Analysis.Verdict);
        Assert.Null(record.HoldUntil);
        Assert.Equal("Lumen.Todo.Contracts.dll", record.Contracts.Assembly);
        Assert.Equal("RunesmithHub.Lumen.Todo.Contracts", record.Contracts.Package);
        Assert.Equal(["network"], record.Capabilities.Detected);
        Assert.Equal(">=0.1.0 <0.2.0", record.RunesmithApi.ToString());
        Assert.Equal("https://github.com/RunesmithHub/packages/releases/download/lumen.todo%401.0.0/lumen.todo-1.0.0.rsplugin", record.Package!.Urls.Single());
        Assert.Equal("https://github.com/RunesmithHub/registry/actions/runs/18000002", record.Build.Run);
        Assert.Equal(18_000_002, record.BuildRun);
        Assert.Equal(64, record.Assets!.Icon.Size64.Width);
        Assert.Equal(501, record.TrackingIssue);
        Assert.Equal("MIT", record.License);
        Assert.Equal("https://github.com/RunesmithHub/registry/issues/501", record.ReportUrl);
        Assert.Null(record.Languages);
        Assert.Null(record.LanguagePack);
    }

    [Fact]
    public async Task CarriesTheLanguagesOfALocalizedPluginAndWhatALanguagePackTranslatesIntoTheIndex()
    {
        var localizedOutput = Samples.WriteBuildOutput(_folder.Combine("localized"), "1.0.0", languages: ["en", "nl"]);
        var packOutput = Samples.WriteBuildOutput(_folder.Combine("pack"), "1.0.1", languagePack: new() { Language = "nl", Name = "Nederlands", EnglishName = "Dutch" });
        var packAnalysis = Samples.Analysis(packOutput, "1.0.1", languagePack: new LanguagePackAnalysis("nl", [new TargetMessages("runesmith", 12), new TargetMessages("runesmith.git", 3)]));

        var localized = await CreateAsync(VersionInputs.Read(Samples.Analysis(localizedOutput, "1.0.0"), Samples.Job("1.0.0"), localizedOutput, 18_000_002));
        var pack = await CreateAsync(VersionInputs.Read(packAnalysis, Samples.Job("1.0.1"), packOutput, 18_000_003));

        Assert.Equal(["en", "nl"], localized.ToIndexRecord().Languages);
        Assert.Null(localized.LanguagePack);
        Assert.Null(pack.Languages);
        Assert.Equivalent(
            new LanguagePackSummary { Language = "nl", Name = "Nederlands", EnglishName = "Dutch", Targets = ["runesmith", "runesmith.git"] }, pack.ToIndexRecord().LanguagePack, strict: true);
    }

    [Fact]
    public async Task HoldsAVersionWithoutTriggersForThePolicysHours()
    {
        await AddPublishedAsync("1.0.0");

        var record = await CreateAsync(Inputs("1.1.0"));

        Assert.Equal(VersionState.Held, record.State);
        Assert.Empty(record.Analysis.Triggers);
        Assert.Equal(AnalysisVerdict.Passed, record.Analysis.Verdict);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 11, 0, 0, TimeSpan.Zero), record.HoldUntil);
    }

    [Fact]
    public async Task AddsTheRegistrysTriggers()
    {
        await AddPublishedAsync("1.0.0", VersionState.Frozen);
        var stranger = new GitHubAccount { Id = 77, Login = "stranger" };

        var record = await CreateAsync(Inputs("1.1.0", [ReviewTriggers.Heuristic], stranger), new Samples.FixedHistory(1234));

        Assert.Equal(
            [ReviewTriggers.MaintainersChanged, ReviewTriggers.Heuristic, ReviewTriggers.ReleaseAuthor, ReviewTriggers.PreviousRestricted],
            record.Analysis.Triggers);
        Assert.Equal(VersionState.Review, record.State);
    }

    [Fact]
    public async Task CountsAModerationEntryOnThePreviousVersionAsRestricted()
    {
        await AddPublishedAsync("1.0.0");
        _registry.Moderation(new RegistryModerationEntry
        {
            Id = 1,
            Target = new ModerationTarget { Kind = ModerationTargetKind.Plugin, Plugin = Samples.PluginId },
            Action = ModerationAction.Freeze,
            Reason = ModerationReason.Broken,
            Note = "Paused.",
            CreatedAt = Samples.Time,
            CreatedBy = 9001,
        });

        var record = await CreateAsync(Inputs("1.1.0"));

        Assert.Contains(ReviewTriggers.PreviousRestricted, record.Analysis.Triggers);
    }

    [Fact]
    public async Task RefusesAPackageForAnotherVersionOrAnExistingRecord()
    {
        await AddPublishedAsync("1.0.0");
        var mismatched = Inputs("1.1.0") with { Job = Samples.Job("1.2.0") };

        await Assert.ThrowsAsync<ToolException>(() => CreateAsync(mismatched));
        await Assert.ThrowsAsync<ToolException>(() => CreateAsync(Inputs("1.0.0")));
    }

    [Fact]
    public async Task RefusesABuildWhoseSbomIsNotTheOneTheAnalysisWrote()
    {
        var inputs = Inputs("1.0.0", [ReviewTriggers.FirstVersion]);
        await File.WriteAllTextAsync(Path.Combine(inputs.OutputDirectory, "lumen.todo-1.0.0.cdx.json"), "{}", TestContext.Current.CancellationToken);
        Samples.Rehash(inputs.OutputDirectory);

        var exception = await Assert.ThrowsAsync<ToolException>(() => CreateAsync(inputs));

        Assert.Contains("is not the one the analysis wrote", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpensTheVersionPullRequestWithTheSummaryAndAutoMerge()
    {
        var result = await new VersionProposer(_github).ProposeAsync(Inputs("1.0.0", [ReviewTriggers.FirstVersion]), "## Passed\n", await _registry.LoadAsync(), new Samples.NoHistory(), IndexState.Empty.Catalog(),
            TestContext.Current.CancellationToken);

        var (number, changes) = Assert.Single(_github.OpenedPullRequests);
        Assert.Equal(number, result.PullRequest);
        Assert.Equal("version/lumen.todo/1.0.0", changes.Branch);
        Assert.Equal("Publish lumen.todo 1.0.0", changes.Title);
        Assert.StartsWith("## Passed", changes.Body, StringComparison.Ordinal);
        Assert.Contains("Tracking issue: #501", changes.Body, StringComparison.Ordinal);
        Assert.True(changes.AutoMerge);
        Assert.Equal([HubLabels.Version], changes.Labels);
        var written = RegistryJson.Deserialize<RegistryVersionRecord>(changes.Files["plugins/lumen.todo/versions/1.0.0.json"]!);
        Assert.Equal(RegistryJson.SerializeToString(result.Record!), RegistryJson.SerializeToString(written));
        Assert.Contains("needs an administrator's review", _github.CommentsOn(501).Single().Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsAFailedVersionOnItsTrackingIssue()
    {
        _github.Issues.Add(new Issue(501, "lumen.todo 1.0.0", "", true, [HubLabels.Tracking], FakeGitHub.Bot, Samples.Time, null, ""));
        var output = Samples.WriteBuildOutput(_folder.Combine("failed"), "1.0.0");
        var analysis = AnalysisReport.Read(Samples.Analysis(output, "1.0.0", AnalysisOutcome.Failed, problems:
        [
            AnalysisProblem.Error("capability-undeclared", "Undeclared capability: process", "lib/Lumen.Todo.dll"),
            AnalysisProblem.Warning("capability-unused", "Declared but unused: network"),
        ]));

        await new VersionProposer(_github).RejectFailedAsync(Samples.Job("1.0.0"), analysis, TestContext.Current.CancellationToken);

        var comment = _github.CommentsOn(501).Single().Body;
        Assert.Contains("- lib/Lumen.Todo.dll: Undeclared capability: process", comment, StringComparison.Ordinal);
        Assert.DoesNotContain("unused", comment, StringComparison.Ordinal);
        Assert.Equal("the automated checks failed.", TrackingIssues.RejectionReason(comment));
        Assert.False(_github.IssueNumber(501).Open);
        Assert.Contains(HubLabels.Rejected, _github.IssueNumber(501).Labels);
        Assert.Empty(_github.OpenedPullRequests);
    }
}
