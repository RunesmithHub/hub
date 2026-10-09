namespace RunesmithHub.Tool.Build;

/// <summary>The names of the files a packaging run writes for a version.</summary>
internal static class BuildArtifacts
{
    public static string Package(string id, string version) => $"{id}-{version}.rsplugin";

    public static string Source(string id, string version) => $"{id}-{version}.source.tar.gz";

    public static string Sbom(string id, string version) => $"{id}-{version}.cdx.json";

    /// <summary>Gets the contracts package's file name from the contracts assembly's, such as <c>RunesmithHub.Lumen.Todo.Contracts.1.4.0.nupkg</c>.</summary>
    public static string ContractsPackage(string contractsAssemblyFile, string version) =>
        ContractsNuGetPackage.FileName(ContractsNuGetPackage.Id(contractsAssemblyFile), version);
}
