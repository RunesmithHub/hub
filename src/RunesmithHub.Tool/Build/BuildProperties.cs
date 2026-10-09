using System.Globalization;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Tool.Build;

/// <summary>
/// The MSBuild properties every hub restore and build uses, forcing deterministic output, locked restore from the hub's NuGet configuration,
/// the minimum Runesmith SDK and the contracts assembly version.
/// </summary>
internal static class BuildProperties
{
    public const string FileName = "hub-build.rsp";

    /// <summary>The MSBuild property plugin projects may use for the Runesmith SDK package version.</summary>
    public const string RunesmithSdkProperty = "RunesmithSdkVersion";

    /// <summary>Gets the properties as command line arguments, one per item, without quoting.</summary>
    /// <exception cref="ToolException">A path can't be written as an MSBuild property value.</exception>
    public static IReadOnlyList<string> Arguments(string repositoryRoot, string nugetConfig, SemanticVersion version, SemanticVersion runesmithSdk)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(runesmithSdk);
        var line = version.Line.Start;
        return
        [
            $"-p:Configuration={ProjectOutput.Configuration}",
            "-p:Deterministic=true",
            "-p:ContinuousIntegrationBuild=true",
            $"-p:PathMap={PropertyPath(repositoryRoot)}=/_/",
            "-p:RestoreLockedMode=true",
            $"-p:RestoreConfigFile={PropertyPath(nugetConfig)}",
            $"-p:{RunesmithSdkProperty}={runesmithSdk}",
            $"-p:Version={version}",
            string.Create(CultureInfo.InvariantCulture, $"-p:AssemblyVersion={line.Major}.{line.Minor}.{line.Patch}.0"),
            string.Create(CultureInfo.InvariantCulture, $"-p:FileVersion={version.Major}.{version.Minor}.{version.Patch}.0"),
        ];
    }

    /// <summary>Writes the arguments as a response file for <c>dotnet restore @file</c> and <c>dotnet build @file</c>, one quoted argument per line.</summary>
    public static string ResponseFile(IReadOnlyList<string> arguments) => string.Concat(arguments.Select(argument => $"\"{argument}\"\n"));

    private static string PropertyPath(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (full.IndexOfAny([',', '=', '"', ';', '%']) >= 0)
            throw new ToolException($"The path {full} has a character MSBuild properties can't hold; build from a simpler path.");
        return full;
    }
}
