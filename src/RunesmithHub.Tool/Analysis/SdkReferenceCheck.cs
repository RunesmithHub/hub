using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Tool.Analysis;

/// <summary>Checks that the plugin was compiled against a Runesmith SDK no newer than the minimum of its API range.</summary>
internal static class SdkReferenceCheck
{
    public const string SdkAssembly = "Runesmith.Sdk";

    public static IEnumerable<AnalysisProblem> Check(PackageSnapshot package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var minimum = package.Manifest.RunesmithApi.Minimum;
        foreach (var assembly in package.Assemblies.Where(assembly => assembly.Role != AssemblyRole.Bundled))
        {
            foreach (var reference in assembly.Definition.MainModule.AssemblyReferences.Where(reference => reference.Name == SdkAssembly))
            {
                var version = reference.Version;
                var referenced = new SemanticVersion(version.Major, version.Minor, Math.Max(0, version.Build));
                if (referenced > minimum)
                {
                    yield return AnalysisProblem.Error(
                        ProblemCodes.SdkReference,
                        $"{assembly.Name} was built against {SdkAssembly} {referenced}, newer than {minimum}, the minimum of runesmithApi.",
                        assembly.File);
                }
            }
        }
    }
}
