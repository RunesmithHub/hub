using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Tests.Versioning;

public sealed class VersionRangeTests
{
    [Theory]
    [InlineData("1.4.2", "1.4.2")]
    [InlineData("=1.4.2", "1.4.2")]
    [InlineData("^1.4.2", ">=1.4.2 <2.0.0")]
    [InlineData("^0.3.1", ">=0.3.1 <0.4.0")]
    [InlineData("^0.0.3", ">=0.0.3 <0.0.4")]
    [InlineData("^0.4", ">=0.4.0 <0.5.0")]
    [InlineData("^1", ">=1.0.0 <2.0.0")]
    [InlineData("~1.4.2", ">=1.4.2 <1.5.0")]
    [InlineData("~0.3", ">=0.3.0 <0.4.0")]
    [InlineData("~0.0.3", ">=0.0.3 <0.1.0")]
    [InlineData(">=1.4.0", ">=1.4.0")]
    [InlineData(">=1.4", ">=1.4.0")]
    [InlineData(">1.4.0", ">1.4.0")]
    [InlineData("<=1.4.0", "<=1.4.0")]
    [InlineData("<2.0.0", "<2.0.0")]
    [InlineData(">=1.4.0 <1.9.0", ">=1.4.0 <1.9.0")]
    [InlineData("<1.9.0 >=1.4.0", ">=1.4.0 <1.9.0")]
    [InlineData(">=1.0.0 >=1.2.0 <3.0.0 <2.5.0", ">=1.2.0 <2.5.0")]
    [InlineData("^1.4.0 <1.6.0", ">=1.4.0 <1.6.0")]
    [InlineData("^1.4.0 || ^2.1.0", ">=1.4.0 <2.0.0 || >=2.1.0 <3.0.0")]
    [InlineData("^2.1.0 || ^1.4.0", ">=1.4.0 <2.0.0 || >=2.1.0 <3.0.0")]
    [InlineData("^1.0.0 || ^1.0.0", ">=1.0.0 <2.0.0")]
    [InlineData("^1.2.3-beta.2", ">=1.2.3-beta.2 <2.0.0")]
    [InlineData(">=1.0.0 <=1.0.0", "1.0.0")]
    public void ParsesEveryFormIntoCanonicalText(string text, string canonical)
    {
        Assert.Equal(canonical, VersionRange.Parse(text).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("x")]
    [InlineData("X")]
    [InlineData("1.x")]
    [InlineData("1.2.x")]
    [InlineData("latest")]
    [InlineData("1.4")]
    [InlineData("=1.4")]
    [InlineData(">1.4")]
    [InlineData("<1.4")]
    [InlineData("<=1.4")]
    [InlineData(">=1.4.0  <2.0.0")]
    [InlineData(" >=1.4.0")]
    [InlineData(">=1.4.0 ")]
    [InlineData(">=2.0.0 <1.0.0")]
    [InlineData(">1.0.0 <1.0.0")]
    [InlineData(">=1.0.0 <1.0.0")]
    [InlineData("1.0.0 2.0.0")]
    [InlineData("^1.0.0 ||")]
    [InlineData("|| ^1.0.0")]
    [InlineData("^1.0.0||^2.0.0")]
    [InlineData(">= 1.0.0")]
    [InlineData("^1.0.0+build")]
    [InlineData("^^1.0.0")]
    public void RejectsInvalidAndUnsatisfiableRanges(string text)
    {
        Assert.False(VersionRange.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Throws<FormatException>(() => VersionRange.Parse(text));
    }

    [Theory]
    [InlineData("1.4.2", "1.4.2", true)]
    [InlineData("1.4.2", "1.4.3", false)]
    [InlineData("^1.4.2", "1.4.2", true)]
    [InlineData("^1.4.2", "1.9.9", true)]
    [InlineData("^1.4.2", "2.0.0", false)]
    [InlineData("^1.4.2", "1.4.1", false)]
    [InlineData("^0.3.1", "0.3.4", true)]
    [InlineData("^0.3.1", "0.4.0", false)]
    [InlineData("^0.3.1", "0.3.0", false)]
    [InlineData("^0.0.3", "0.0.3", true)]
    [InlineData("^0.0.3", "0.0.4", false)]
    [InlineData("~1.4.2", "1.4.9", true)]
    [InlineData("~1.4.2", "1.5.0", false)]
    [InlineData(">=1.4.0", "7.0.0", true)]
    [InlineData(">1.4.0", "1.4.0", false)]
    [InlineData("<=1.4.0", "1.4.0", true)]
    [InlineData("<1.4.0", "1.4.0", false)]
    [InlineData(">=1.4.0 <1.9.0", "1.8.9", true)]
    [InlineData(">=1.4.0 <1.9.0", "1.9.0", false)]
    [InlineData("^1.4.0 || ^2.1.0", "2.0.0", false)]
    [InlineData("^1.4.0 || ^2.1.0", "2.1.5", true)]
    [InlineData("^1.4.0 || ^2.1.0", "1.4.0", true)]
    public void ContainsTheVersionsItsComparatorsAllow(string range, string version, bool expected)
    {
        Assert.Equal(expected, VersionRange.Parse(range).Contains(SemanticVersion.Parse(version)));
    }

    [Theory]
    [InlineData("^1.2.3-beta.2", "1.2.3-beta.3", true)]
    [InlineData("^1.2.3-beta.2", "1.2.3-beta.1", false)]
    [InlineData("^1.2.3-beta.2", "1.2.4-beta.1", false)]
    [InlineData("^1.2.3-beta.2", "1.2.3", true)]
    [InlineData("^1.2.3", "1.3.0-beta", false)]
    [InlineData(">=1.0.0 <2.0.0", "2.0.0-beta", false)]
    [InlineData(">=1.0.0-alpha <1.0.0", "1.0.0-beta", true)]
    [InlineData("1.5.0-beta.2", "1.5.0-beta.2", true)]
    [InlineData("^1.0.0 || >=2.0.0-rc.1 <2.1.0", "2.0.0-rc.2", true)]
    [InlineData("^1.0.0 || >=2.0.0-rc.1 <2.1.0", "1.5.0-rc.2", false)]
    public void MatchesPreReleasesOnlyWhenAComparatorInTheSetNamesOneWithTheSameNumbers(string range, string version, bool expected)
    {
        Assert.Equal(expected, VersionRange.Parse(range).Contains(SemanticVersion.Parse(version)));
    }

    [Theory]
    [InlineData("^1.2.3", "1.3.0-beta")]
    [InlineData(">=1.0.0 <2.0.0", "1.5.0-rc.1")]
    public void MatchesAnyPreReleaseInBoundsWhenAllowed(string range, string version)
    {
        Assert.True(VersionRange.Parse(range).Contains(SemanticVersion.Parse(version), allowPreRelease: true));
    }

    [Theory]
    [InlineData(">=1.4.0", ">=1.4.0 <2.0.0")]
    [InlineData(">=1.4", ">=1.4.0 <2.0.0")]
    [InlineData(">1.4.0", ">1.4.0 <2.0.0")]
    [InlineData(">=0.3.1", ">=0.3.1 <0.4.0")]
    [InlineData(">=0.0.3", ">=0.0.3 <0.0.4")]
    [InlineData(">=1.2.0-beta", ">=1.2.0-beta <2.0.0")]
    [InlineData("^1.4.0", ">=1.4.0 <2.0.0")]
    [InlineData(">=1.4.0 <1.9.0", ">=1.4.0 <1.9.0")]
    [InlineData("1.4.0", "1.4.0")]
    [InlineData("<2.0.0", "<2.0.0")]
    [InlineData(">=1.0.0 || ^3.0.0", ">=1.0.0 <2.0.0 || >=3.0.0 <4.0.0")]
    public void NormalisesMinimumsToTheEndOfTheirLine(string text, string normalised)
    {
        var range = VersionRange.Parse(text).Normalize();

        Assert.Equal(normalised, range.ToString());
        Assert.True(range.IsBounded);
        Assert.Equal(range, VersionRange.Parse(range.ToString()));
    }

    [Theory]
    [InlineData(">=1.4", ">=1.4.0 <2.0.0")]
    [InlineData("^0.3.1", ">=0.3.1 <0.4.0")]
    [InlineData("1.2.3", "1.2.3")]
    public void NormalisesRangeTextForBuildTooling(string text, string normalised)
    {
        Assert.True(VersionRange.TryNormalize(text, out var result, out var error), error);
        Assert.Equal(normalised, result);
    }

    [Fact]
    public void ExplainsWhyRangeTextCannotBeNormalised()
    {
        Assert.False(VersionRange.TryNormalize("*", out var result, out var error));
        Assert.Null(result);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("^0.1.0", true)]
    [InlineData("^1.4.2", true)]
    [InlineData(">=1.4.0", true)]
    [InlineData("~1.4.2", true)]
    [InlineData("1.4.2", true)]
    [InlineData(">=0.4.0 <=0.4.9", true)]
    [InlineData("^0.0.3", true)]
    [InlineData(">=1.4.0 <2.0.0", true)]
    [InlineData(">=1.4.0 <=2.0.0", false)]
    [InlineData(">=1.4.0 <2.0.1", false)]
    [InlineData("^1.0.0 || ^2.0.0", false)]
    [InlineData(">=0.3.0 <0.5.0", false)]
    [InlineData("<0.5.0", false)]
    [InlineData("^1.0.0 || ~1.5.0", true)]
    public void KnowsWhetherANormalisedRangeStaysWithinOneLine(string text, bool oneLine)
    {
        var range = VersionRange.Parse(text).Normalize();

        Assert.Equal(oneLine, range.StaysWithinOneLine);
        Assert.Equal(!oneLine, range.CrossesLines);
    }

    [Theory]
    [InlineData("^1.4.0", "1.x")]
    [InlineData(">=1.4.0 <3.0.0", "1.x,2.x")]
    [InlineData(">=1.4.0 <=3.0.0", "1.x,2.x,3.x")]
    [InlineData("^1.4.0 || ^3.1.0", "1.x,3.x")]
    [InlineData(">=0.2.5 <0.5.0", "0.2,0.3,0.4")]
    [InlineData(">=0.0.2 <0.0.4", "0.0.2,0.0.3")]
    public void ListsTheLinesARangeCovers(string text, string lines)
    {
        Assert.True(VersionRange.Parse(text).TryGetCoveredLines(out var covered));
        Assert.Equal(lines, string.Join(',', covered.Select(line => line.ToString())));
    }

    [Theory]
    [InlineData(">=1.0.0")]
    [InlineData(">=0.0.1 <0.2.0")]
    public void RefusesToListUnboundedOrEndlessLines(string text)
    {
        Assert.False(VersionRange.Parse(text).TryGetCoveredLines(out _));
        Assert.True(VersionRange.Parse(text).CrossesLines);
    }

    [Theory]
    [InlineData("^1.4.0", "1.4.0")]
    [InlineData("^1.4.0 || ^0.9.0", "0.9.0")]
    [InlineData("<2.0.0", "0.0.0")]
    [InlineData(">1.2.0", "1.2.0")]
    public void HasTheLowestLowerBoundAsItsMinimum(string text, string minimum)
    {
        Assert.Equal(SemanticVersion.Parse(minimum), VersionRange.Parse(text).Minimum);
    }

    [Fact]
    public void RangesWithTheSameCanonicalTextAreEqual()
    {
        Assert.Equal(VersionRange.Parse("^1.4.0"), VersionRange.Parse(">=1.4.0 <2.0.0"));
        Assert.Equal(VersionRange.Parse("^1.4.0").GetHashCode(), VersionRange.Parse(">=1.4.0 <2.0.0").GetHashCode());
        Assert.NotEqual(VersionRange.Parse("^1.4.0"), VersionRange.Parse("~1.4.0"));
    }

    [Fact]
    public void ExactlyHoldsOneVersion()
    {
        var range = VersionRange.Exactly(SemanticVersion.Parse("2.0.0-beta.1"));

        Assert.Equal("2.0.0-beta.1", range.ToString());
        Assert.True(range.Contains(SemanticVersion.Parse("2.0.0-beta.1")));
        Assert.False(range.Contains(SemanticVersion.Parse("2.0.0")));
    }
}
