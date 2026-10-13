using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Tool.Analysis;

/// <summary>Checks that the plugin was compiled against Runesmith's shared assemblies no newer than the minimum of its API range.</summary>
/// <remarks>The Runesmith assemblies Runesmith shares with plugins, such as Runesmith.Sdk and Runesmith.Text, carry the plugin API version, so
/// a reference to a newer one fails to load on the oldest Runesmith the range allows. The libraries a plugin carries are built against them
/// too, so they count.</remarks>
internal static class SdkReferenceCheck
{
    public const string SdkAssembly = "Runesmith.Sdk";

    public static IEnumerable<AnalysisProblem> Check(PackageSnapshot package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var minimum = package.Manifest.RunesmithApi.Minimum;
        foreach (var assembly in package.Assemblies)
        {
            foreach (var reference in assembly.Definition.MainModule.AssemblyReferences.Where(reference => IsVersionedWithApi(reference.Name)))
            {
                var version = reference.Version;
                var referenced = new SemanticVersion(version.Major, version.Minor, Math.Max(0, version.Build));
                if (referenced > minimum)
                {
                    yield return AnalysisProblem.Error(
                        ProblemCodes.SdkReference,
                        $"{assembly.Name} was built against {reference.Name} {referenced}, newer than {minimum}, the minimum of runesmithApi.",
                        assembly.File);
                }
            }
        }
    }

    private static bool IsVersionedWithApi(string name) => name.StartsWith("Runesmith.", StringComparison.Ordinal) && HostAssemblies.IsHostShared(name);
}
