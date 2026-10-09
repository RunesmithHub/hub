using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Analysis.Api;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Analysis;

public sealed class ApiTests
{
    private const string Before = """
        namespace Lumen.Todo.Contracts
        {
            public interface ITodoService { int Count { get; } string Find(string text); }
            public enum Priority { Low = 0, High = 1 }
            public sealed record Todo(string Text, Priority Priority);
            public abstract class Exporter { protected Exporter() { } public abstract void Export(); internal void Hidden() { } }
            public static class Names { public const string Service = "lumen.todo"; }
        }
        """;

    private static ApiSurface Surface(string source) => ApiSurface.Read(Assemblies.Load(Compiler.Compile("Lumen.Todo.Contracts", source)).Definition);

    private static ApiDiff Diff(string after) => ApiDiff.Compare(Surface(Before), Surface(after));

    [Fact]
    public void FindsNoChangeInTheSameApi()
    {
        var diff = Diff(Before.Replace("internal void Hidden() { }", "internal void Hidden() { } private int _cache;", StringComparison.Ordinal));

        Assert.Empty(diff.Breaking);
        Assert.Empty(diff.Additions);
    }

    [Theory]
    [InlineData("string Find(string text);", "", "Removed", "Lumen.Todo.Contracts.ITodoService.Find(System.String)")]
    [InlineData("High = 1", "High = 2", "Changed", "Lumen.Todo.Contracts.Priority.High")]
    [InlineData("public const string Service = \"lumen.todo\";", "public const string Service = \"lumen.todos\";", "Changed", "Lumen.Todo.Contracts.Names.Service")]
    [InlineData("public sealed record Todo", "internal sealed record Todo", "Removed", "Lumen.Todo.Contracts.Todo")]
    [InlineData("public abstract void Export();", "public abstract void Export(); public abstract void Flush();", "Added", "Lumen.Todo.Contracts.Exporter.Flush()")]
    [InlineData("int Count { get; }", "int Count { get; } bool IsEmpty { get; }", "Added", "Lumen.Todo.Contracts.ITodoService.IsEmpty")]
    public void TreatsRemovedChangedAndNewAbstractMembersAsBreaking(string from, string to, string kind, string member)
    {
        var diff = Diff(Before.Replace(from, to, StringComparison.Ordinal));

        Assert.Contains(diff.Breaking, change => change.Kind.ToString() == kind && change.Member == member);
    }

    [Fact]
    public void TreatsNewTypesAndMembersAsAdditions()
    {
        var diff = Diff(Before.Replace("public static class Names {", "public interface IExtra { } public static class Names { public const string Extra = \"x\";", StringComparison.Ordinal));

        Assert.Empty(diff.Breaking);
        Assert.Equal(["Lumen.Todo.Contracts.IExtra", "Lumen.Todo.Contracts.Names.Extra"], diff.Additions.Select(change => change.Member));
    }

    [Fact]
    public void TreatsStricterGenericConstraintsAsChanges()
    {
        var before = Surface("namespace C { public interface IStore<T> { } }");
        var after = Surface("namespace C { public interface IStore<T> where T : class { } }");

        Assert.Equal(ApiChangeKind.Changed, Assert.Single(ApiDiff.Compare(before, after).Breaking).Kind);
    }

    [Theory]
    [InlineData("1.2.0", "1.3.0", true, false, null)]
    [InlineData("1.2.0", "1.2.1", true, false, ProblemCodes.ApiAddition)]
    [InlineData("1.2.0", "1.9.0", false, true, ProblemCodes.ApiBreaking)]
    [InlineData("1.2.0", "2.0.0", false, true, null)]
    [InlineData("0.3.1", "0.3.2", true, false, null)]
    [InlineData("0.3.1", "0.3.2", false, true, ProblemCodes.ApiBreaking)]
    [InlineData("0.3.1", "0.4.0", false, true, null)]
    [InlineData("0.0.3", "0.0.4", false, true, null)]
    [InlineData("1.2.0", "1.2.1-beta.1", false, true, null)]
    public void RequiresTheBumpTheChangesNeed(string previous, string current, bool added, bool broken, string? problem)
    {
        var change = new ApiChange(ApiChangeKind.Added, "M", null, "m");
        var diff = new ApiDiff(broken ? [change] : [], added ? [change] : []);

        var problems = VersionBumpCheck.Check(SemanticVersion.Parse(previous), SemanticVersion.Parse(current), diff);

        Assert.Equal(problem, problems.SingleOrDefault()?.Code);
    }
}
