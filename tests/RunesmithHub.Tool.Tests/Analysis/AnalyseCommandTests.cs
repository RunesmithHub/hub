using System.Text;
using System.Text.Json;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Tool.Analysis.Heuristics;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Analysis.Vulnerabilities;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Findings;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Analysis;

public sealed class AnalyseCommandTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();
    private readonly ToolRunner _tool = new() { Http = new FakeHttp().Answer(OsvDatabase.BatchEndpoint.AbsoluteUri, Encoding.UTF8.GetBytes("""{ "results": [{}] }""")) };

    public AnalyseCommandTests()
    {
        File.WriteAllBytes(_folder.Combine("registration.json"), RegistryJson.Serialize(TestRegistrations.For("lumen.todo", TestPackage.Repository)));
        new TestPackage().Write(_folder.Combine("previous.rsplugin"));
    }

    public void Dispose() => _folder.Dispose();

    private Task<(int ExitCode, string Output, string Error)> AnalyseAsync(TestPackage package, params string[] extra)
    {
        var source = package.WriteSource(_folder.Combine("source"));
        foreach (var ((id, version), bytes) in package.NuGetPackages())
            ((FakeHttp)_tool.Http!).Answer($"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/{version}/{id.ToLowerInvariant()}.{version}.nupkg", bytes);
        return _tool.RunAsync(["analyse", "--package", package.Write(_folder.Combine("current.rsplugin")), "--previous", _folder.Combine("previous.rsplugin"),
            "--registration", _folder.Combine("registration.json"), "--source", source.Repository, "--commit", source.Commit, "--output", _folder.Combine("out"), .. extra]);
    }

    [Fact]
    public async Task WritesTheReportAndSummaryAndPrintsTheSummary()
    {
        var (exitCode, output, error) = await AnalyseAsync(new TestPackage { Version = "1.0.1", Locked = [("Markdig", "0.40.0", "BSD-2-Clause")] });

        Assert.True(exitCode == 0, error);
        using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(_folder.Combine("out", AnalysisReport.FileName), TestContext.Current.CancellationToken));
        Assert.Equal("review", report.RootElement.GetProperty("verdict").GetString());
        Assert.Equal("new-nuget-package", report.RootElement.GetProperty("triggers")[0].GetString());
        Assert.Equal("skipped", report.RootElement.GetProperty("heuristics").GetString());
        Assert.Equal("2026-10-09T12:00:00Z", report.RootElement.GetProperty("analyzedAt").GetString());
        Assert.Equal(AnalysisOutcome.Review, AnalysisReport.Read(await File.ReadAllBytesAsync(_folder.Combine("out", AnalysisReport.FileName), TestContext.Current.CancellationToken)).Verdict);
        Assert.Equal(await File.ReadAllTextAsync(_folder.Combine("out", SummaryWriter.FileName), TestContext.Current.CancellationToken), output);
        Assert.False(File.Exists(_folder.Combine("out", PrivateFindings.FileName)));
        Assert.True(File.Exists(_folder.Combine("out", CycloneDxSbom.FileName)));
        Assert.Contains("pkg:nuget/Markdig@0.40.0", Assert.Single(((FakeHttp)_tool.Http!).Requests).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExitsWithOneWhenTheVersionFails()
    {
        var (exitCode, output, _) = await AnalyseAsync(new TestPackage { Version = "1.0.1", ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { public object Get() => new System.Net.Http.HttpClient(); } }" });

        Assert.Equal(1, exitCode);
        Assert.StartsWith("# Analysis of lumen.todo 1.0.1\n\n**Verdict: failed**\n\n## Failures\n\n- The plugin uses the network capability without declaring it", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunsHeuristicsFromTheRulesFileInTheEnvironmentAndWritesTheirDetailsPrivately()
    {
        File.WriteAllText(_folder.Combine("rules.json"), """{ "rules": [{ "id": "marker", "kind": "string", "pattern": "lumen-marker" }] }""");
        _tool.Environment[HeuristicRuleSet.FileVariable] = _folder.Combine("rules.json");

        var (exitCode, output, _) = await AnalyseAsync(new TestPackage { Version = "1.0.1", ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { public string M => \"lumen-marker\"; } }" });

        Assert.Equal(0, exitCode);
        Assert.Contains("A review rule matched. An administrator will look at the details.", output, StringComparison.Ordinal);
        Assert.DoesNotContain("marker\"", output, StringComparison.Ordinal);
        var findings = await File.ReadAllTextAsync(_folder.Combine("out", PrivateFindings.FileName), TestContext.Current.CancellationToken);
        Assert.Contains("\"rule\": \"marker\"", findings, StringComparison.Ordinal);
        Assert.Contains("Lumen.Todo.TodoService.get_M", findings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SealsTheFindingsToTheAdministratorKeyThatOnlyItsPrivateKeyOpens()
    {
        File.WriteAllText(_folder.Combine("rules.json"), """{ "rules": [{ "id": "marker", "kind": "string", "pattern": "lumen-marker" }] }""");
        _tool.Environment[HeuristicRuleSet.FileVariable] = _folder.Combine("rules.json");
        _tool.Environment["KEY_PASSPHRASE"] = "correct horse battery staple";
        Assert.Equal(0, (await _tool.RunAsync("keys", "generate", "--out", _folder.Combine("admin.pem"), "--passphrase-env", "KEY_PASSPHRASE")).ExitCode);
        _tool.Environment[FindingsCipher.RecipientVariable] = await File.ReadAllTextAsync(_folder.Combine("admin.pub.json"), TestContext.Current.CancellationToken);

        var (exitCode, _, _) = await AnalyseAsync(new TestPackage { Version = "1.0.1", ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { public string M => \"lumen-marker\"; } }" });

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(_folder.Combine("out", PrivateFindings.FileName)));
        var sealedFile = _folder.Combine("out", SealedFindings.FileName);
        Assert.DoesNotContain("marker", await File.ReadAllTextAsync(sealedFile, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var (decryptExit, findings, error) = await _tool.RunAsync("findings", "decrypt", "--key", _folder.Combine("admin.pem"), "--passphrase-env", "KEY_PASSPHRASE", sealedFile);
        Assert.True(decryptExit == 0, error);
        Assert.Contains("\"rule\": \"marker\"", findings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesARecipientThatIsNotAPublicKey()
    {
        _tool.Environment[FindingsCipher.RecipientVariable] = "not a key";

        var (exitCode, _, error) = await AnalyseAsync(new TestPackage { Version = "1.0.1" });

        Assert.Equal(1, exitCode);
        Assert.Contains("PublicKey", error, StringComparison.Ordinal);
    }
}
