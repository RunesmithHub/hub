using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Analysis;

public sealed class SummaryWriterTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private async Task<string> SummaryAsync(TestPackage current, TestPackage? previous)
    {
        var request = new AnalysisRequest(
            current.Write(_folder.Combine("current.rsplugin")), previous?.Write(_folder.Combine("previous.rsplugin")), TestRegistrations.For("lumen.todo", TestPackage.Repository), current.WriteSource(_folder.Path), null);
        return SummaryWriter.Write((await new PluginAnalyzer(new FakeVulnerabilities(), current.PackageSource(), TimeProvider.System).AnalyseAsync(request, TestContext.Current.CancellationToken)).Report);
    }

    [Fact]
    public async Task LeadsWithTheVerdictThenCapabilityDependencyAndBuildChangesAndTheComparison()
    {
        var current = new TestPackage
        {
            Version = "1.1.0",
            ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService : Lumen.Todo.Contracts.ITodoService { public int Count => System.IO.File.ReadAllLines(\"todo.txt\").Length; } }",
            Capabilities = [("filesystem", "Reads todo.txt files.")],
            Dependencies = [("harbor.issues", ">=1.2.0")],
        };
        File.WriteAllText(_folder.Combine("Directory.Build.targets"), "<Project><Target Name=\"Stamp\" /></Project>");

        var summary = await SummaryAsync(current, new TestPackage());

        Assert.Equal(
            """
            # Analysis of lumen.todo 1.1.0

            **Verdict: passed, needs review**

            ## Needs review because

            - It declares or uses a capability the previous version did not.
            - Its build runs commands or custom tasks.

            ## Capabilities

            | Capability | Declared | Detected | Change |
            | --- | --- | --- | --- |
            | filesystem | yes | yes | added |

            Where filesystem is used:

            - `Lumen.Todo.TodoService.get_Count` in Lumen.Todo.dll uses `System.IO.File.ReadAllLines`

            ## Contracts since 1.0.0

            No public API changes.

            ## Dependencies

            - Plugin harbor.issues added at >=1.2.0 <2.0.0

            ## Maintainers and build logic

            Maintainer changes are checked when the version record is written.

            - target Stamp in Directory.Build.targets line 1

            ## Review rules

            Review rules were not run.

            ## Changes since the previous version

            [Compare 1.0.0 with 1.1.0](https://github.com/lumen/todo/compare/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa...aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa)

            <!-- comparison-stats -->

            """.ReplaceLineEndings("\n"),
            summary);
    }

    [Fact]
    public async Task ListsTheDependenciesOfAFirstVersion()
    {
        var markdig = Compiler.Compile("Markdig", "namespace Markdig { public sealed class Markdown { } }");

        var summary = await SummaryAsync(new TestPackage { Bundled = [("Markdig", markdig, "Markdig", "0.40.0")], Locked = [("Markdig", "0.40.0", "BSD-2-Clause"), ("Runesmith.Sdk", "0.1.0", null)] }, null);

        Assert.Contains("Plugins: none.\n\nBundled NuGet packages: Markdig 0.40.0.\n", summary, StringComparison.Ordinal);
        Assert.Contains("This is the first version.", summary, StringComparison.Ordinal);
    }
}
