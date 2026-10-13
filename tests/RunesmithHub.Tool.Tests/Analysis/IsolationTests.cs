using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Isolation;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Analysis;

public sealed class IsolationTests : IDisposable
{
    private static readonly byte[] Markdown = Compiler.Compile("Markdown.Lib", "namespace Markdown.Lib { public sealed class Document { } }");
    private static readonly byte[] HarborContracts = Compiler.Compile("Harbor.Issues.Contracts", "namespace Harbor.Issues.Contracts { public interface IIssues { } }");

    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private IReadOnlyList<AnalysisProblem> Check(TestPackage package)
    {
        using var snapshot = PackageSnapshot.Open(package.Write(_folder.Combine($"{Guid.NewGuid():N}.rsplugin")), PackageLimits.Default);
        return [.. IsolationCheck.Check(snapshot), .. SdkReferenceCheck.Check(snapshot)];
    }

    [Fact]
    public void AcceptsAPluginThatKeepsItsBoundaries()
    {
        var package = new TestPackage
        {
            ContractsSource = "namespace Lumen.Todo.Contracts { public interface ITodoService { Harbor.Issues.Contracts.IIssues Issues { get; } } }",
            ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { public object Read() => new Markdown.Lib.Document(); public object Issues(Harbor.Issues.Contracts.IIssues issues) => issues; } }",
            References = [HarborContracts],
            Dependencies = [("harbor.issues", "^1.2.0")],
            Bundled = [("Markdown.Lib", Markdown, "Markdown.Lib", "1.0.0")],
            Locked = [("Markdown.Lib", "1.0.0", "MIT")],
        };

        Assert.Empty(Check(package));
    }

    [Fact]
    public void RejectsContractsThatReferenceAPackageOrExposeItsTypes()
    {
        var package = new TestPackage
        {
            ContractsSource = "namespace Lumen.Todo.Contracts { public interface ITodoService { Markdown.Lib.Document Read(); } }",
            ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { } }",
            References = [Markdown],
            Bundled = [("Markdown.Lib", Markdown, "Markdown.Lib", "1.0.0")],
            Locked = [("Markdown.Lib", "1.0.0", "MIT")],
        };

        var problems = Check(package);

        Assert.Contains(problems, problem => problem.Code == ProblemCodes.ContractsReference && problem.Message.Contains("references Markdown.Lib", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Code == ProblemCodes.ExposedType && problem.Message.Contains("Markdown.Lib.Document", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUseOfAnotherPluginsContractsWithoutDeclaringIt()
    {
        var package = new TestPackage
        {
            ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { public object Issues(Harbor.Issues.Contracts.IIssues issues) => issues; } }",
            References = [HarborContracts],
        };

        var problem = Assert.Single(Check(package));

        Assert.Equal(ProblemCodes.ImplementationReference, problem.Code);
        Assert.Contains("does not declare", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsReferencesNothingWillResolve()
    {
        var other = Compiler.Compile("Harbor.Issues", "namespace Harbor.Issues { public sealed class Internals { } }");
        var package = new TestPackage
        {
            ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { public object Reach() => new Harbor.Issues.Internals(); } }",
            References = [other],
        };

        Assert.Contains(Check(package), problem => problem.Code == ProblemCodes.ImplementationReference && problem.Message.Contains("Harbor.Issues,", StringComparison.Ordinal));
    }

    [Fact]
    public void AllowsInternalsOnlyToTheOwnTestAssembly()
    {
        var package = new TestPackage
        {
            ImplementationSource = """
                [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Lumen.Todo.Tests")]
                [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Harbor.Issues")]
                namespace Lumen.Todo { public sealed class TodoService { } }
                """,
        };

        var problem = Assert.Single(Check(package));

        Assert.Equal(ProblemCodes.InternalsVisibleTo, problem.Code);
        Assert.Contains("Harbor.Issues", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresTheContractsToBeNamedAfterTheImplementation()
    {
        var problem = Assert.Single(Check(new TestPackage { ContractsName = "Lumen.Api" }));

        Assert.Equal(ProblemCodes.AssemblyName, problem.Code);
        Assert.Contains("it must be Lumen.Todo.Contracts", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FlagsContractsThatHoldMoreThanAnApiForReview()
    {
        var package = new TestPackage
        {
            ContractsSource = """
                namespace Lumen.Todo.Contracts
                {
                    public sealed record Todo(string Text);
                    public static class Registry { public static int Count; public static int Next() => Count++; }
                    [System.Composition.Export]
                    public sealed class Part { }
                    public interface IDefaults { int Size => 4; }
                }
                """,
            ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { } }",
            References = [Compiler.Compile("System.Composition.AttributedModel", "namespace System.Composition { public sealed class ExportAttribute : System.Attribute { } }")],
        };

        var problems = Check(package).Where(problem => problem.Code == ProblemCodes.ContractsContent).ToList();

        Assert.All(problems, problem => Assert.Equal(ProblemSeverity.Review, problem.Severity));
        Assert.Contains(problems, problem => problem.Message.Contains("MEF part Lumen.Todo.Contracts.Part", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Message.Contains("static mutable state in Lumen.Todo.Contracts.Registry.Count", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Message.Contains("Registry has methods with logic: Next", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, problem => problem.Message.Contains("Todo ", StringComparison.Ordinal) || problem.Message.Contains("IDefaults", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsBuildingAgainstANewerSdkThanTheApiMinimum()
    {
        var package = new TestPackage
        {
            ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { public object? Store(Runesmith.Sdk.Shell.ISecretStore store) => store; } }",
            References = [Compiler.SdkStub("0.1.4")],
            RunesmithApi = "^0.1.2",
        };

        var problem = Assert.Single(Check(package));

        Assert.Equal(ProblemCodes.SdkReference, problem.Code);
        Assert.Contains("Runesmith.Sdk 0.1.4, newer than 0.1.2", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsBuildingAgainstANewerRunesmithTextThanTheApiMinimum()
    {
        var package = new TestPackage
        {
            ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { public object Make() => new Runesmith.Text.Buffer(); } }",
            References = [TextStub("0.2.1")],
            RunesmithApi = "^0.2.0",
        };

        var problem = Assert.Single(Check(package));

        Assert.Equal(ProblemCodes.SdkReference, problem.Code);
        Assert.Contains("Lumen.Todo was built against Runesmith.Text 0.2.1, newer than 0.2.0", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsABundledLibraryBuiltAgainstANewerRunesmithAssemblyThanTheApiMinimum()
    {
        var syntax = Compiler.Compile("Lumen.Syntax", "namespace Lumen.Syntax { public static class Parser { public static object Make() => new Runesmith.Text.Buffer(); } }", TextStub("0.2.1"));
        var package = new TestPackage
        {
            ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { } }",
            Bundled = [("Lumen.Syntax", syntax, "Lumen.Syntax", "1.0.0")],
            Locked = [("Lumen.Syntax", "1.0.0", "MIT")],
            RunesmithApi = "^0.2.0",
        };

        var problem = Assert.Single(Check(package), problem => problem.Code == ProblemCodes.SdkReference);

        Assert.Contains("Lumen.Syntax was built against Runesmith.Text 0.2.1", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsBuildingAgainstTheApiMinimum()
    {
        var package = new TestPackage
        {
            ImplementationSource = "namespace Lumen.Todo { public sealed class TodoService { public object Make() => new Runesmith.Text.Buffer(); } }",
            References = [TextStub("0.2.0")],
            RunesmithApi = "^0.2.0",
        };

        Assert.Empty(Check(package));
    }

    private static byte[] TextStub(string version) => Compiler.Compile("Runesmith.Text", $$"""
        [assembly: System.Reflection.AssemblyVersion("{{version}}.0")]
        namespace Runesmith.Text { public sealed class Buffer { } }
        """);
}
