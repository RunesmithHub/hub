using RunesmithHub.Tool.Registrations;

namespace RunesmithHub.Tool.Tests.Registrations;

public sealed class NameSimilarityTests
{
    [Theory]
    [InlineData("lurnen", "lumen", "looks the same")]
    [InlineData("l0umen", "loumen", "looks the same")]
    [InlineData("lumens", "lumen", "within 1 character")]
    [InlineData("lumen-official", "lumen", "adds \"official\"")]
    [InlineData("prolumen", "lumen", "adds \"pro\"")]
    [InlineData("Lumen Labs", "lumen-labs", "is the same")]
    [InlineData("harbour-issuez", "harbor-issues", "within 2 characters")]
    public void FlagsLookalikes(string candidate, string existing, string reason) =>
        Assert.Contains(reason, NameSimilarity.Compare(candidate, existing), StringComparison.Ordinal);

    [Theory]
    [InlineData("atlas", "lumen")]
    [InlineData("charts", "themes")]
    public void LeavesDifferentNamesAlone(string candidate, string existing) => Assert.Null(NameSimilarity.Compare(candidate, existing));

    [Fact]
    public void MeasuresEditDistance()
    {
        Assert.Equal(3, NameSimilarity.Distance("kitten", "sitting"));
        Assert.Equal(0, NameSimilarity.Distance("same", "same"));
    }
}
