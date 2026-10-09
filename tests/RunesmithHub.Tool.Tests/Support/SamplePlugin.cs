using RunesmithHub.Tool.Fixture;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>
/// Writes a small two-project plugin and a stub of the Runesmith SDK to disk. The implementation uses an HTTP client and the SDK's secret
/// store; the manifest declares credentials and filesystem but not network, unless asked to.
/// </summary>
internal static class SamplePlugin
{
    public const string Id = "lumen.sample";
    public const string Version = "1.0.0";

    public static void WriteSdkStub(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Runesmith.Sdk.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <Version>0.1.0</Version>
                <AssemblyVersion>0.1.0.0</AssemblyVersion>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, "ISecretStore.cs"), """
            namespace Runesmith.Sdk.Shell;

            public interface ISecretStore
            {
                System.Threading.Tasks.Task<string?> GetAsync(string key, System.Threading.CancellationToken cancellationToken = default);
            }
            """);
    }

    public static void WritePlugin(string root, string sdkAssembly, bool declareNetwork)
    {
        Directory.CreateDirectory(Path.Combine(root, "Lumen.Sample.Contracts"));
        Directory.CreateDirectory(Path.Combine(root, "Lumen.Sample"));
        var network = declareNetwork ? """{ "id": "network", "reason": "Fetches greetings." }, """ : "";
        var hosts = declareNetwork ? """  "networkHosts": ["example.com"],""" + "\n" : "";
        File.WriteAllText(Path.Combine(root, "plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "id": "{{Id}}",
              "name": "Lumen Sample",
              "version": "{{Version}}",
              "summary": "Greets people.",
              "authors": ["Lumen"],
              "license": "MIT",
              "repository": "https://github.com/lumen/sample",
              "runesmithApi": "^0.1.0",
              "projects": {
                "contracts": "Lumen.Sample.Contracts/Lumen.Sample.Contracts.csproj",
                "implementation": "Lumen.Sample/Lumen.Sample.csproj"
              },
              "capabilities": [{{network}}{ "id": "credentials", "reason": "Keeps your token." }, { "id": "filesystem", "reason": "Reads files." }],
            {{hosts}}  "icon": "icon.png",
              "categories": ["Other"]
            }
            """);
        File.WriteAllText(Path.Combine(root, "LICENSE"), "MIT License\n");
        File.WriteAllText(Path.Combine(root, "README.md"), "# Lumen Sample\n");
        File.WriteAllBytes(Path.Combine(root, "icon.png"), IconRenderer.RenderPng("#3366ff", 'L', 256));

        var reference = $"""
                <Reference Include="Runesmith.Sdk">
                  <HintPath>{sdkAssembly}</HintPath>
                  <Private>false</Private>
                </Reference>
            """;
        File.WriteAllText(Path.Combine(root, "Lumen.Sample.Contracts", "Lumen.Sample.Contracts.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                <GenerateDocumentationFile>true</GenerateDocumentationFile>
                <NoWarn>CS1591</NoWarn>
              </PropertyGroup>
              <ItemGroup>
            {reference}
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Lumen.Sample.Contracts", "IGreeter.cs"), """
            namespace Lumen.Sample.Contracts;

            public interface IGreeter
            {
                string Greet(string name);
            }
            """);
        File.WriteAllText(Path.Combine(root, "Lumen.Sample", "Lumen.Sample.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Lumen.Sample.Contracts\Lumen.Sample.Contracts.csproj" />
            {reference}
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Lumen.Sample", "Greeter.cs"), """
            using Lumen.Sample.Contracts;
            using Runesmith.Sdk.Shell;

            namespace Lumen.Sample;

            public sealed class Greeter(ISecretStore secrets) : IGreeter
            {
                private static readonly System.Net.Http.HttpClient Http = new();

                public string Greet(string name) => $"Hello, {name}";

                public async System.Threading.Tasks.Task<string> FetchAsync()
                {
                    var token = await secrets.GetAsync("lumen.sample/token");
                    return await Http.GetStringAsync("https://example.com/" + token);
                }
            }
            """);
    }
}
