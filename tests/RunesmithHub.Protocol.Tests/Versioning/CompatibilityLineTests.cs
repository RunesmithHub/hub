using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Tests.Versioning;

public sealed class CompatibilityLineTests
{
    [Theory]
    [InlineData("1.4.2", "1.0.0", "2.0.0")]
    [InlineData("0.3.1", "0.3.0", "0.4.0")]
    [InlineData("0.0.3", "0.0.3", "0.0.4")]
    public void StartsAndEndsWhereSemanticVersioningBreaks(string version, string start, string nextStart)
    {
        var line = SemanticVersion.Parse(version).Line;

        Assert.Equal(SemanticVersion.Parse(start), line.Start);
        Assert.Equal(SemanticVersion.Parse(nextStart), line.NextStart);
    }

    [Fact]
    public void ContainsTheVersionsOfItsLineOnly()
    {
        var line = SemanticVersion.Parse("0.3.0").Line;

        Assert.True(line.Contains(SemanticVersion.Parse("0.3.9")));
        Assert.True(line.Contains(SemanticVersion.Parse("0.3.0-beta")));
        Assert.False(line.Contains(SemanticVersion.Parse("0.4.0")));
        Assert.False(line.Contains(SemanticVersion.Parse("1.3.0")));
    }
}
