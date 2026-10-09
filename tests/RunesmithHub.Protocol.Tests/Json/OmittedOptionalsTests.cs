using System.Text;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Registry;

namespace RunesmithHub.Protocol.Tests.Json;

public sealed class OmittedOptionalsTests
{
    private const string Sha = "0000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void APackagedManifestWithoutOptionalListsReadsThemAsEmpty()
    {
        var json =
            "{\"schemaVersion\":1,\"id\":\"lumen.todo\",\"name\":\"Lumen Todo\",\"version\":\"1.0.0\",\"summary\":\"Todos.\",\"authors\":[\"Lumen\"]," +
            "\"license\":\"MIT\",\"repository\":\"https://github.com/lumen/todo\",\"runesmithApi\":\">=0.1.0 <0.2.0\",\"categories\":[\"Productivity\"]," +
            "\"assemblies\":{\"contracts\":{\"file\":\"A.Contracts.dll\",\"sha256\":\"" + Sha + "\"},\"implementation\":{\"file\":\"A.dll\",\"sha256\":\"" + Sha + "\"}}," +
            "\"build\":{\"commit\":\"" + Sha[..40] + "\",\"run\":\"local\",\"sdk\":\"10.0.400\"}}";

        var manifest = IndexJson.Deserialize<PackagedManifest>(Encoding.UTF8.GetBytes(json));

        Assert.NotNull(manifest.Dependencies);
        Assert.Empty(manifest.Dependencies);
        Assert.NotNull(manifest.Capabilities);
        Assert.Empty(manifest.Capabilities);
        Assert.NotNull(manifest.NetworkHosts);
        Assert.Empty(manifest.NetworkHosts);
        Assert.NotNull(manifest.Platforms);
        Assert.Empty(manifest.Platforms);
        Assert.NotNull(manifest.Keywords);
        Assert.Empty(manifest.Keywords);
        Assert.NotNull(manifest.Assemblies.Bundled);
        Assert.Empty(manifest.Assemblies.Bundled);
    }

    [Fact]
    public void ABundledAssemblyWithoutAPackageIsBuiltFromTheRepository()
    {
        var json =
            "{\"schemaVersion\":1,\"id\":\"lumen.todo\",\"name\":\"Lumen Todo\",\"version\":\"1.0.0\",\"summary\":\"Todos.\",\"authors\":[\"Lumen\"]," +
            "\"license\":\"MIT\",\"repository\":\"https://github.com/lumen/todo\",\"runesmithApi\":\">=0.1.0 <0.2.0\",\"categories\":[\"Productivity\"]," +
            "\"assemblies\":{\"contracts\":{\"file\":\"A.Contracts.dll\",\"sha256\":\"" + Sha + "\"},\"implementation\":{\"file\":\"A.dll\",\"sha256\":\"" + Sha + "\"}," +
            "\"bundled\":[{\"file\":\"A.Helpers.dll\",\"sha256\":\"" + Sha + "\"}]}," +
            "\"build\":{\"commit\":\"" + Sha[..40] + "\",\"run\":\"local\",\"sdk\":\"10.0.400\"}}";

        var bundled = Assert.Single(IndexJson.Deserialize<PackagedManifest>(Encoding.UTF8.GetBytes(json)).Assemblies.Bundled);

        Assert.Null(bundled.Package);
        Assert.Null(bundled.PackageVersion);
        Assert.True(bundled.IsBuiltFromRepository);
    }

    [Fact]
    public void ARegistryPolicyWithoutMirrorsReadsThemAsEmpty()
    {
        var policy = RegistryJson.Deserialize<RegistryPolicy>("{\"holdHours\":24}"u8);

        Assert.NotNull(policy.Mirrors);
        Assert.Empty(policy.Mirrors);
    }

    [Fact]
    public void ARegistryPolicyWithoutHoldHoursHoldsForTheDefault() =>
        Assert.Equal(RegistryPolicy.DefaultHoldHours, RegistryJson.Deserialize<RegistryPolicy>("{\"mirrors\":[]}"u8).HoldHours);

    [Fact]
    public void ARegistrationWithoutPathOrTagPrefixUsesTheRootAndV()
    {
        var registration = RegistryJson.Deserialize<Registration>(
            """
            {
              "id": "lumen.todo",
              "repository": { "id": 1, "url": "https://github.com/lumen/todo" },
              "maintainers": [{ "id": 2, "login": "lumen" }],
              "tier": "unverified",
              "state": "listed",
              "registeredAt": "2026-10-09T12:00:00Z"
            }
            """u8);

        Assert.Equal(".", registration.Repository.Path);
        Assert.Equal("v", registration.Repository.TagPrefix);
    }
}
