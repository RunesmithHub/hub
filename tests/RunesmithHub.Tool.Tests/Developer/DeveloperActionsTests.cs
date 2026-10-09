using System.Text;
using Microsoft.Extensions.Time.Testing;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Developer;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Developer;

public sealed class DeveloperActionsTests : IDisposable
{
    private readonly TestRegistry _registry = new();
    private readonly TemporaryFolder _folder = new();
    private readonly FakeGitHub _github = new();

    public DeveloperActionsTests()
    {
        _registry.Publisher(Samples.Publisher()).Registration(Samples.Registration()).Registration(Samples.Registration("lumen.notes", 7002, "lumen-labs/notes"));
        var owner = _github.AddUser(Samples.OwnerId, "lumen-labs", OwnerType.Organization);
        _github.AddRepository(Samples.RepositoryId, Samples.RepositoryName, owner);
        _github.AddUser(Samples.Maintainer.Id, Samples.Maintainer.Login);
        _github.AddUser(6001, "grace");
        _github.Issues.Add(new Tool.GitHub.Issue(80, "Request", "", true, [], Samples.Maintainer, Samples.Time, null, ""));
    }

    public void Dispose()
    {
        _folder.Dispose();
        _registry.Dispose();
    }

    private static IssueForm Form(string label, string body, long author = 5001) =>
        new(80, "Request", author, [label], IssueForm.ParseBody(body));

    private async Task<DeveloperOutcome> RunAsync(IssueForm form) =>
        await new DeveloperActions(_github, _github, new FakeTimeProvider(Samples.Time)).RunAsync(form, await _registry.LoadAsync(), TestContext.Current.CancellationToken);

    private T Written<T>(string path)
        where T : class => RegistryJson.Deserialize<T>(_github.OpenedPullRequests.Single().Changes.Files[path]!);

    private async Task AddVersionAsync(string version, VersionState state)
    {
        var record = await Samples.RecordAsync(await _registry.LoadAsync(), Samples.WriteBuildOutput(_folder.Combine(version), version), version);
        _registry.Version(Samples.PluginId, record with { State = state });
    }

    [Fact]
    public void ParsesIssueFormSections()
    {
        var fields = IssueForm.ParseBody("### Plugin ID\n\nlumen.todo\n\n### Reason\n\nIt deletes files.\nSecond line.\n\n### Replacement plugin ID\n\n_No response_\n");

        Assert.Equal("lumen.todo", fields["Plugin ID"]);
        Assert.Equal("It deletes files.\nSecond line.", fields["Reason"]);
        Assert.Equal("", fields["Replacement plugin ID"]);
    }

    [Fact]
    public void ReadsTheIssueOfAnIssuesEvent()
    {
        var form = IssueForm.Read(Encoding.UTF8.GetBytes("""
            { "number": 12, "title": "Yank", "user": { "id": 5001, "login": "ada" }, "labels": [ { "name": "yank" } ], "body": "### Plugin ID\n\nlumen.todo" }
            """));

        Assert.Equal((12, 5001L, "lumen.todo"), (form.Number, form.AuthorId, form.Field("Plugin ID")));
        Assert.Equal(["yank"], form.Labels);
    }

    [Fact]
    public async Task OnlyMaintainersMayAct()
    {
        var outcome = await RunAsync(Form("yank", "### Plugin ID\n\nlumen.todo\n\n### Version\n\n1.0.0\n\n### Reason\n\nBad.", author: 6001));

        Assert.Contains("Only the maintainers", outcome.Reply, StringComparison.Ordinal);
        Assert.False(_github.IssueNumber(80).Open);
        Assert.Empty(_github.OpenedPullRequests);
    }

    [Fact]
    public async Task YanksAVersionWithTheReason()
    {
        await AddVersionAsync("1.0.0", VersionState.Published);

        var outcome = await RunAsync(Form("yank", "### Plugin ID\n\nlumen.todo\n\n### Version\n\n1.0.0\n\n### Reason\n\nCorrupts settings."));

        var record = Written<RegistryVersionRecord>("plugins/lumen.todo/versions/1.0.0.json");
        Assert.Equal((VersionState.Yanked, "Corrupts settings."), (record.State, record.YankReason));
        var changes = _github.OpenedPullRequests.Single().Changes;
        Assert.Contains("Closes #80", changes.Body, StringComparison.Ordinal);
        Assert.True(changes.AutoMerge);
        Assert.NotNull(outcome.PullRequest);
    }

    [Fact]
    public async Task UnyanksOnlyAYankedVersion()
    {
        await AddVersionAsync("1.0.0", VersionState.Published);
        var refused = await RunAsync(Form("unyank", "### Plugin ID\n\nlumen.todo\n\n### Version\n\n1.0.0"));
        Assert.Contains("is not yanked", refused.Reply, StringComparison.Ordinal);

        await AddVersionAsync("1.1.0", VersionState.Yanked);
        var outcome = await RunAsync(Form("unyank", "### Plugin ID\n\nlumen.todo\n\n### Version\n\n1.1.0"));
        Assert.Contains("administrator", outcome.Reply, StringComparison.Ordinal);
        Assert.Equal(VersionState.Published, Written<RegistryVersionRecord>("plugins/lumen.todo/versions/1.1.0.json").State);
    }

    [Fact]
    public async Task DeprecatesWithAReplacementAndUndeprecates()
    {
        await RunAsync(Form("deprecate", "### Plugin ID\n\nlumen.todo\n\n### Reason\n\nMerged into Lumen Notes.\n\n### Replacement plugin ID\n\nlumen.notes"));
        var deprecated = Written<Protocol.Registry.Registration>("plugins/lumen.todo/registration.json");
        Assert.Equal((RegistrationState.Deprecated, "lumen.notes"), (deprecated.State, deprecated.Deprecated!.Replacement));

        _registry.Registration(deprecated);
        _github.OpenedPullRequests.Clear();
        await RunAsync(Form("undeprecate", "### Plugin ID\n\nlumen.todo"));
        var listed = Written<Protocol.Registry.Registration>("plugins/lumen.todo/registration.json");
        Assert.Equal((RegistrationState.Listed, (Deprecation?)null), (listed.State, listed.Deprecated));
    }

    [Fact]
    public async Task RefusesAnUnknownReplacement()
    {
        var outcome = await RunAsync(Form("deprecate", "### Plugin ID\n\nlumen.todo\n\n### Reason\n\nOld.\n\n### Replacement plugin ID\n\nlumen.todo"));

        Assert.Contains("not another registered plugin", outcome.Reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangesMaintainersByPermanentId()
    {
        await RunAsync(Form("maintainers", "### Plugin ID\n\nlumen.todo\n\n### Add maintainers\n\n@grace\n\n### Remove maintainers\n\n_No response_"));

        var maintainers = Written<Protocol.Registry.Registration>("plugins/lumen.todo/registration.json").Maintainers;
        Assert.Equal([5001L, 6001L], maintainers.Select(maintainer => maintainer.Id));
    }

    [Fact]
    public async Task PublishNowNamesThePluginForDiscovery()
    {
        var outcome = await RunAsync(Form("publish-now", "### Plugin ID\n\nlumen.todo"));

        Assert.Equal("lumen.todo", outcome.PublishNow);
        Assert.False(_github.IssueNumber(80).Open);
    }

    [Fact]
    public async Task VerificationRequestsPostTheChecklist()
    {
        await AddVersionAsync("1.0.0", VersionState.Published);

        var outcome = await RunAsync(Form("verification", "### Plugin ID\n\nlumen.todo"));

        Assert.Contains("- [x] The plugin has a published version", outcome.Reply, StringComparison.Ordinal);
        Assert.Contains("- [x] The default branch blocks force pushes", outcome.Reply, StringComparison.Ordinal);
        Assert.Contains("@RunesmithHub/admins", outcome.Reply, StringComparison.Ordinal);
        Assert.Empty(_github.OpenedPullRequests);
    }

    [Fact]
    public async Task DeleteAndTransfer()
    {
        await RunAsync(Form("delete", "### Plugin ID\n\nlumen.todo\n\n### Reason\n\nAbandoned."));
        Assert.Equal(RegistrationState.Deleted, Written<Protocol.Registry.Registration>("plugins/lumen.todo/registration.json").State);

        var transfer = await RunAsync(Form("transfer", "### Plugin ID\n\nlumen.todo\n\n### New publisher\n\nlumen"));
        Assert.Contains("Both publishers confirm", transfer.Reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandRefusesAMismatchedAuthor()
    {
        using var folder = new TemporaryFolder();
        var issue = folder.Combine("issue.json");
        await File.WriteAllTextAsync(issue, """{ "number": 1, "title": "x", "user": { "id": 1 }, "labels": [], "body": "" }""", TestContext.Current.CancellationToken);
        var tool = new ToolRunner();

        var (exitCode, _, error) = await tool.RunAsync("developer", "--issue", issue, "--author-id", "2", "--registry", _registry.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("does not match", error, StringComparison.Ordinal);
    }
}
