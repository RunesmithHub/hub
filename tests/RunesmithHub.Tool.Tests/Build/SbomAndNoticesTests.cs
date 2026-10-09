using System.Text.Json;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Tests.Build;

public sealed class SbomAndNoticesTests
{
    private static readonly LockedPackage[] Locked =
    [
        new("Runesmith.Sdk", "0.1.0", Convert.ToBase64String(new byte[64])),
        new("Markdig", "0.40.0", Convert.ToBase64String(Enumerable.Repeat((byte)1, 64).ToArray())),
    ];

    private static readonly BundledPackage[] Bundled =
    [
        new("Markdig", "0.40.0", "", new PackageLicense("BSD-2-Clause", "Copyright (c) Alexandre Mutel\r\nRedistribution is allowed.", null), "Copyright Alexandre Mutel", "https://github.com/xoofx/markdig", "Alexandre Mutel"),
    ];

    [Fact]
    public void WritesAValidCycloneDxBomWithPackageUrlsHashesAndScopes()
    {
        var sbom = CycloneDxSbom.Create("lumen.todo", "1.0.0", new string('a', 40), "https://github.com/lumen/todo", Locked, Bundled);

        using var document = JsonDocument.Parse(sbom);
        var root = document.RootElement;
        Assert.Equal("CycloneDX", root.GetProperty("bomFormat").GetString());
        Assert.Equal("1.6", root.GetProperty("specVersion").GetString());
        Assert.StartsWith("urn:uuid:", root.GetProperty("serialNumber").GetString(), StringComparison.Ordinal);
        Assert.Equal("lumen.todo", root.GetProperty("metadata").GetProperty("component").GetProperty("name").GetString());
        var markdig = root.GetProperty("components")[0];
        Assert.Equal("pkg:nuget/Markdig@0.40.0", markdig.GetProperty("purl").GetString());
        Assert.Equal("required", markdig.GetProperty("scope").GetString());
        Assert.Equal(Convert.ToHexStringLower(Enumerable.Repeat((byte)1, 64).ToArray()), markdig.GetProperty("hashes")[0].GetProperty("content").GetString());
        Assert.Equal("BSD-2-Clause", markdig.GetProperty("licenses")[0].GetProperty("expression").GetString());
        Assert.Equal("excluded", root.GetProperty("components")[1].GetProperty("scope").GetString());
        Assert.Equal(sbom, CycloneDxSbom.Create("lumen.todo", "1.0.0", new string('a', 40), "https://github.com/lumen/todo", Locked, Bundled));
    }

    [Fact]
    public void ReadsBackTheComponentsTheAnalysisNeeds()
    {
        var components = SbomComponent.Read(CycloneDxSbom.Create("lumen.todo", "1.0.0", new string('a', 40), "https://github.com/lumen/todo", Locked, Bundled));

        Assert.Equal(
            [new SbomComponent("Markdig", "0.40.0", "pkg:nuget/Markdig@0.40.0", true, "BSD-2-Clause"), new SbomComponent("Runesmith.Sdk", "0.1.0", "pkg:nuget/Runesmith.Sdk@0.1.0", false, null)],
            components);
    }

    [Fact]
    public void ListsEachBundledPackageWithItsLicenseInTheNotices()
    {
        var notices = ThirdPartyNotices.Create("Lumen Todo", Bundled);

        Assert.StartsWith("# Third-party notices\n\nLumen Todo bundles the packages below.", notices, StringComparison.Ordinal);
        Assert.Contains("## Markdig 0.40.0\n\n- License: BSD-2-Clause\n- Project: https://github.com/xoofx/markdig\n- Copyright: Copyright Alexandre Mutel\n", notices, StringComparison.Ordinal);
        Assert.Contains("```text\nCopyright (c) Alexandre Mutel\nRedistribution is allowed.\n```\n", notices, StringComparison.Ordinal);
        Assert.Equal("# Third-party notices\n\nLumen Todo bundles no third-party packages.\n", ThirdPartyNotices.Create("Lumen Todo", []));
    }
}
