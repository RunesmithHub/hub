using System.Text;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Registrations;
using RunesmithHub.Tool.Registry;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Registrations;

public sealed class RegistrationValidatorTests : IDisposable
{
    private const long AtlasId = 4001;
    private const long RepositoryId = 8001;
    private static readonly GitHubAccount Author = new() { Id = AtlasId, Login = "atlas-dev" };

    private readonly TestRegistry _registry = new();
    private readonly FakeGitHub _github = new();
    private readonly string _base;

    public RegistrationValidatorTests()
    {
        _registry.InitGit();
        _registry.Publisher(Samples.Publisher()).Registration(Samples.Registration());
        _base = _registry.Commit("Existing registry");
        var owner = _github.AddUser(AtlasId, "atlas-dev");
        _github.AddRepository(RepositoryId, "atlas-dev/atlas-rest", owner);
        _github.Files["atlas-dev/atlas-rest:plugin.json"] = Manifest("atlas.rest", "https://github.com/atlas-dev/atlas-rest", "MIT");
    }

    public void Dispose() => _registry.Dispose();

    private static PublisherFile Atlas(string name = "atlas") => new()
    {
        Name = name,
        DisplayName = "Atlas Dev",
        Owner = new PublisherOwner { Id = AtlasId, Login = "atlas-dev", Type = OwnerType.User },
        State = PublisherState.Active,
    };

    private static Protocol.Registry.Registration AtlasRest(string id = "atlas.rest", params GitHubAccount[] maintainers) =>
        Samples.Registration(id, RepositoryId, "atlas-dev/atlas-rest", state: RegistrationState.Registered, maintainers: maintainers.Length == 0 ? [Author] : maintainers);

    private static byte[] Manifest(string id, string repository, string license) => Encoding.UTF8.GetBytes($$"""
        {
          "schemaVersion": 1, "id": "{{id}}", "name": "Atlas REST", "version": "0.6.0", "summary": "HTTP requests.", "authors": ["Atlas"],
          "license": "{{license}}", "repository": "{{repository}}", "runesmithApi": "^0.1.0",
          "projects": { "contracts": "src/C/C.csproj", "implementation": "src/I/I.csproj" }, "icon": "icon.png", "categories": ["Testing"]
        }
        """);

    private async Task<ValidationReport> ValidateAsync(Action change, GitHubAccount? author = null)
    {
        change();
        _registry.Commit("Register");
        var changes = await _registry.Git.DiffAsync(_base, "HEAD", TestContext.Current.CancellationToken);
        return await new RegistrationValidator(_github).ValidateAsync(
            new DirectoryRegistryFiles(_registry.Path), new GitRegistryFiles(_registry.Git, _base), changes, author ?? Author, TestContext.Current.CancellationToken);
    }

    private static IEnumerable<string> Lines(ValidationReport report, CheckLevel level, string check) =>
        report.Lines.Where(line => line.Level == level && line.Check == check).Select(line => line.Message);

    [Fact]
    public async Task PassesAValidRegistrationWithANewPublisher()
    {
        var report = await ValidateAsync(() => _registry.Publisher(Atlas()).Registration(AtlasRest()));

        Assert.False(report.Failed, report.Render());
        Assert.DoesNotContain(report.Lines, line => line.Level == CheckLevel.Warn);
        Assert.Contains("The registration check passed.", report.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailsReservedNamesAndWarnsAboutLookalikes()
    {
        var report = await ValidateAsync(() => _registry
            .Publisher(Atlas("official"))
            .Registration(AtlasRest("official.tod0")));

        Assert.True(report.Failed);
        Assert.NotEmpty(Lines(report, CheckLevel.Fail, "Reserved names"));
        Assert.Contains(Lines(report, CheckLevel.Warn, "Similarity"), line => line.Contains("looks the same as \"todo\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailsAForkPrivateOrMismatchedRepository()
    {
        var owner = _github.Users[AtlasId];
        _github.AddRepository(RepositoryId, "atlas-dev/atlas-rest", owner, fork: true);
        _github.Files["atlas-dev/atlas-rest:plugin.json"] = Manifest("atlas.other", "https://github.com/atlas-dev/atlas-rest", "Proprietary");

        var report = await ValidateAsync(() => _registry.Publisher(Atlas()).Registration(AtlasRest()));

        var problems = Lines(report, CheckLevel.Fail, "Repository").ToList();
        Assert.Contains(problems, line => line.Contains("is a fork", StringComparison.Ordinal));
        Assert.Contains(problems, line => line.Contains("plugin.json: id", StringComparison.Ordinal));
        Assert.Contains(problems, line => line.Contains("plugin.json: license", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailsWhenTheRepositoryBelongsToSomeoneElse()
    {
        var stranger = _github.AddUser(4242, "stranger");
        _github.AddRepository(RepositoryId, "atlas-dev/atlas-rest", stranger);

        var report = await ValidateAsync(() => _registry.Publisher(Atlas()).Registration(AtlasRest()));

        Assert.Single(Lines(report, CheckLevel.Fail, "Ownership"));
    }

    [Fact]
    public async Task ChecksTheAuthorsAuthorityAndTheMaintainersAccess()
    {
        var helper = _github.AddUser(4002, "helper");
        _github.Permissions["atlas-dev/atlas-rest:helper"] = RepositoryPermission.Read;
        var report = await ValidateAsync(() => _registry.Publisher(Atlas()).Registration(AtlasRest(maintainers: [Author, new GitHubAccount { Id = helper.Id, Login = "helper" }])),
            author: new GitHubAccount { Id = 4003, Login = "drive-by" });

        Assert.NotEmpty(Lines(report, CheckLevel.Fail, "Authority"));
        Assert.Contains(Lines(report, CheckLevel.Fail, "Maintainers"), line => line.Contains("@helper", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WarnsWhenTheTokenCannotReadPermissions()
    {
        var helper = _github.AddUser(4002, "helper");
        _github.Permissions["atlas-dev/atlas-rest:helper"] = RepositoryPermission.Unknown;

        var report = await ValidateAsync(() => _registry.Publisher(Atlas()).Registration(AtlasRest(maintainers: [Author, new GitHubAccount { Id = helper.Id, Login = "helper" }])));

        Assert.False(report.Failed, report.Render());
        Assert.Contains(Lines(report, CheckLevel.Warn, "Maintainers"), line => line.Contains("reviewer confirms", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailsASecondRegistrationOfTheSameRepositoryFolder()
    {
        var report = await ValidateAsync(() => _registry.Publisher(Atlas()).Registration(AtlasRest()).Registration(AtlasRest("atlas.rest-two")));

        Assert.NotEmpty(Lines(report, CheckLevel.Fail, "One plugin per repository"));
    }

    [Fact]
    public async Task FailsMalformedAndDeletedRecords()
    {
        var report = await ValidateAsync(() =>
        {
            _registry.Write("publishers/atlas.json", "{ \"name\": \"atlas\", \"surprise\": true }"u8.ToArray());
            File.Delete(Path.Combine(_registry.Path, "publishers/lumen.json"));
        });

        Assert.Equal(2, Lines(report, CheckLevel.Fail, "Format").Count());
    }

    [Fact]
    public async Task CommandPrintsTheReportAndFailsWithExitCodeOne()
    {
        _registry.Publisher(Atlas("hub"));
        _registry.Commit("Reserved");
        var tool = new ToolRunner();
        tool.GitHub.AddUser(AtlasId, "atlas-dev");
        tool.Environment["HUB_PR_AUTHOR_ID"] = "4001";
        tool.Environment["HUB_PR_AUTHOR_LOGIN"] = "atlas-dev";

        var (exitCode, output, _) = await tool.RunAsync("registration", "validate", "--base", _base, "--head", _registry.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("FAIL  Reserved names", output, StringComparison.Ordinal);
    }
}
