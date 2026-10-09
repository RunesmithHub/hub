using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Tests.Versioning;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("0.0.0")]
    [InlineData("1.4.2")]
    [InlineData("10.20.30")]
    [InlineData("1.5.0-beta.2")]
    [InlineData("1.0.0-alpha")]
    [InlineData("1.0.0-0.3.7")]
    [InlineData("1.0.0-x.7.z.92")]
    [InlineData("1.0.0-x-y-z.--")]
    public void ParsesValidVersionsAndWritesThemBack(string text)
    {
        Assert.Equal(text, SemanticVersion.Parse(text).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-a..b")]
    [InlineData("1.2.3-a_b")]
    [InlineData("1.2.3+build.1")]
    [InlineData("v1.2.3")]
    [InlineData(" 1.2.3")]
    [InlineData("1.2.x")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.99999999999")]
    public void RejectsInvalidVersions(string text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
        Assert.Throws<FormatException>(() => SemanticVersion.Parse(text));
    }

    [Fact]
    public void OrdersVersionsAsSemanticVersioningSays()
    {
        string[] ascending =
        [
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1",
            "1.1.0", "2.0.0", "10.0.0",
        ];
        var versions = ascending.Select(SemanticVersion.Parse).ToList();

        for (var i = 0; i < versions.Count - 1; i++)
        {
            Assert.True(versions[i] < versions[i + 1], $"{versions[i]} < {versions[i + 1]}");
            Assert.True(versions[i + 1] > versions[i]);
        }

        Assert.Equal(ascending, versions.OrderByDescending(v => v).Reverse().Select(v => v.ToString()));
    }

    [Fact]
    public void NumericPreReleaseIdentifiersCompareNumericallyAndBelowAlphanumericOnes()
    {
        Assert.True(SemanticVersion.Parse("1.0.0-2") < SemanticVersion.Parse("1.0.0-10"));
        Assert.True(SemanticVersion.Parse("1.0.0-999") < SemanticVersion.Parse("1.0.0-a"));
    }

    [Fact]
    public void EqualVersionsAreEqualAndHashAlike()
    {
        var left = SemanticVersion.Parse("1.2.3-beta.1");
        var right = new SemanticVersion(1, 2, 3, "beta", "1");

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, SemanticVersion.Parse("1.2.3"));
    }

    [Fact]
    public void ExposesItsParts()
    {
        var version = SemanticVersion.Parse("2.5.7-rc.1");

        Assert.Equal((2, 5, 7), (version.Major, version.Minor, version.Patch));
        Assert.Equal(["rc", "1"], version.PreRelease);
        Assert.True(version.IsPreRelease);
        Assert.Equal(SemanticVersion.Parse("2.5.7"), version.WithoutPreRelease);
    }

    [Theory]
    [InlineData("2.4.1", "2.x")]
    [InlineData("1.0.0", "1.x")]
    [InlineData("0.4.2", "0.4")]
    [InlineData("0.1.0", "0.1")]
    [InlineData("0.0.3", "0.0.3")]
    [InlineData("0.0.0", "0.0.0")]
    [InlineData("1.0.0-beta", "1.x")]
    public void BelongsToItsCompatibilityLine(string version, string line)
    {
        Assert.Equal(line, SemanticVersion.Parse(version).Line.ToString());
    }
}
