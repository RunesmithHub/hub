namespace RunesmithHub.Tool.Tests.Support;

/// <summary>System.Data.DataSetExtensions 4.5.0 exactly as nuget.org serves it, author and repository signed (MIT licensed).</summary>
internal static class SignedNuGet
{
    public const string Id = "System.Data.DataSetExtensions";
    public const string Version = "4.5.0";
    public const string AssemblyPath = "lib/netstandard2.0/System.Data.DataSetExtensions.dll";

    /// <summary>The content hash restore records for it, from its <c>.nupkg.metadata</c> and every lock file that pins it.</summary>
    public const string ContentHash = "221clPs1445HkTBZPL+K9sDBdJRB8UN8rgjO3ztB0CQ26z//fmJXtlsr6whGatscsKGBrhJl5bwJuKSA8mwFOw==";

    public static byte[] Bytes() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "system.data.datasetextensions.4.5.0.nupkg"));
}
