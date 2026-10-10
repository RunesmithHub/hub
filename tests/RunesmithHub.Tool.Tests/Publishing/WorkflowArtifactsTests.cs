using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Publishing;

namespace RunesmithHub.Tool.Tests.Publishing;

public sealed class WorkflowArtifactsTests
{
    [Fact]
    public void NamesEachVersionsArtifactsApartFromTheOthersInTheSameRun()
    {
        var version = SemanticVersion.Parse("0.1.0");

        Assert.Equal("package-lumen.todo-0.1.0", WorkflowArtifacts.Package("lumen.todo", version));
        Assert.Equal("analysis-lumen.todo-0.1.0", WorkflowArtifacts.Analysis("lumen.todo", version));
        Assert.NotEqual(WorkflowArtifacts.Package("lumen.todo", version), WorkflowArtifacts.Package("ember.themes", version));
        Assert.NotEqual(WorkflowArtifacts.Package("lumen.todo", version), WorkflowArtifacts.Package("lumen.todo", SemanticVersion.Parse("0.1.1")));
    }
}
