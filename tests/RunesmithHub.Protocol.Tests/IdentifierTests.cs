namespace RunesmithHub.Protocol.Tests;

public sealed class IdentifierTests
{
    [Theory]
    [InlineData("ab")]
    [InlineData("harbor")]
    [InlineData("a-b-c")]
    [InlineData("x1")]
    [InlineData("a234567890123456789012345678901234567890")]
    public void AcceptsValidPublisherNames(string name)
    {
        Assert.True(PublisherName.IsValid(name));
        Assert.Equal(name, PublisherName.Parse(name).Value);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1ab")]
    [InlineData("-ab")]
    [InlineData("ab-")]
    [InlineData("a--b")]
    [InlineData("Ab")]
    [InlineData("a_b")]
    [InlineData("a.b")]
    [InlineData("a2345678901234567890123456789012345678901")]
    [InlineData("")]
    public void RejectsInvalidPublisherNames(string name)
    {
        Assert.False(PublisherName.IsValid(name));
        Assert.Throws<FormatException>(() => PublisherName.Parse(name));
    }

    [Fact]
    public void SplitsAPluginIdIntoPublisherAndName()
    {
        var id = PluginId.Parse("lumen.todo");

        Assert.Equal("lumen", id.Publisher);
        Assert.Equal("todo", id.Name);
        Assert.Equal("lumen.todo", id.ToString());
    }

    [Theory]
    [InlineData("lumen")]
    [InlineData("lumen.")]
    [InlineData(".todo")]
    [InlineData("lumen.todo.extra")]
    [InlineData("Lumen.todo")]
    [InlineData("lumen.t")]
    [InlineData("lumen.to--do")]
    public void RejectsInvalidPluginIds(string text)
    {
        Assert.False(PluginId.IsValid(text));
    }

    [Theory]
    [InlineData("Runesmith.Sdk", true)]
    [InlineData("Runesmith.LanguageServices", true)]
    [InlineData("runesmith.sdk", true)]
    [InlineData("Avalonia.Controls", true)]
    [InlineData("Avalonia", true)]
    [InlineData("System.Composition.Hosting", true)]
    [InlineData("MessagePack.Annotations", true)]
    [InlineData("Microsoft.VisualStudio.Composition", true)]
    [InlineData("SkiaSharp", true)]
    [InlineData("SkiaSharp.Views", false)]
    [InlineData("Lumen.Todo", false)]
    [InlineData("Microsoft.Extensions.Logging", false)]
    public void KnowsTheHostSharedAssemblies(string name, bool shared)
    {
        Assert.Equal(shared, HostAssemblies.IsHostShared(name));
    }

    [Fact]
    public void ListsTheCapabilitiesAndCategories()
    {
        Assert.Equal(["network", "process", "filesystem", "environment", "credentials", "native", "dynamic-code"], Capabilities.All);
        Assert.Equal(16, Categories.Default.Count);
        Assert.Equal("Languages", Categories.Default[0]);
        Assert.Equal("Other", Categories.Default[^1]);
    }
}
