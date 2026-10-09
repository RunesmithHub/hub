using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Tests.Support;

namespace RunesmithHub.Tool.Tests.Analysis;

public sealed class LicenseAndBuildLogicTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static SbomComponent Bundled(string id, string? license) => new(id, "1.0.0", $"pkg:nuget/{id}@1.0.0", true, license);

    [Theory]
    [InlineData("MIT", "MIT", null)]
    [InlineData("MIT", "Apache-2.0 OR GPL-3.0-only", null)]
    [InlineData("MIT", "GPL-3.0-only", ProblemCodes.LicenseCombination)]
    [InlineData("MIT", "AGPL-3.0-or-later", ProblemCodes.LicenseCombination)]
    [InlineData("GPL-3.0-or-later", "GPL-3.0-only", null)]
    [InlineData("MIT", "LGPL-2.1-only", null)]
    [InlineData("GPL-2.0-only", "Apache-2.0", ProblemCodes.LicenseCombination)]
    [InlineData("MIT", null, ProblemCodes.LicenseUnknown)]
    public void FlagsLicensesThatMayNotCombine(string plugin, string? package, string? code)
    {
        var problems = LicenseCheck.Check(plugin, [Bundled("Some.Package", package), new SbomComponent("Build.Only", "1.0.0", "pkg:nuget/Build.Only@1.0.0", false, "GPL-3.0-only")]).ToList();

        Assert.Equal(code, problems.SingleOrDefault()?.Code);
    }

    [Fact]
    public void RejectsAPluginLicenseThatIsNotOsiApproved() =>
        Assert.Equal(ProblemCodes.License, Assert.Single(LicenseCheck.Check("Proprietary", [])).Code);

    [Fact]
    public void ListsTargetsExecTasksAndInlineTasksInEveryMsBuildFile()
    {
        Directory.CreateDirectory(_folder.Combine("src", "obj"));
        File.WriteAllText(_folder.Combine("Directory.Build.props"), """
            <Project>
              <UsingTask TaskName="Sneaky" TaskFactory="RoslynCodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll">
                <Task><Code Type="Fragment" Language="cs">System.Console.WriteLine();</Code></Task>
              </UsingTask>
            </Project>
            """);
        File.WriteAllText(_folder.Combine("src", "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <Target Name="Fetch" BeforeTargets="Build">
                <Exec Command="curl https://example.com" />
              </Target>
            </Project>
            """);
        File.WriteAllText(_folder.Combine("src", "obj", "App.csproj.nuget.g.targets"), "<Project><Target Name=\"Generated\" /></Project>");
        File.WriteAllText(_folder.Combine("src", "Plain.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Nullable>enable</Nullable></PropertyGroup></Project>");

        var findings = BuildLogicScanner.Scan(_folder.Path);

        Assert.Equal(
            [
                new BuildLogicFinding("Directory.Build.props", 2, "task", "Sneaky (inline)"),
                new BuildLogicFinding("src/App.csproj", 2, "target", "Fetch"),
                new BuildLogicFinding("src/App.csproj", 3, "exec", "in target Fetch"),
            ],
            findings);
    }
}
