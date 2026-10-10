using System.Text;
using System.Text.Json;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Protocol.Signing;
using RunesmithHub.Protocol.Tests.Support;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Tests.Json;

public sealed class JsonFormatTests
{
    [Fact]
    public void WritesIndexPayloadsCompactlyWithTheSpecFieldNames()
    {
        var payload = new TimestampPayload
        {
            Type = PayloadTypes.Timestamp,
            Version = 3,
            Expires = new DateTimeOffset(2026, 10, 12, 8, 30, 15, 999, TimeSpan.FromHours(2)),
            Snapshot = new FileMeta { Version = 4, Sha256 = new string('a', 64), Length = 10 },
            Moderation = new FileMeta { Version = 2, Sha256 = new string('b', 64), Length = 20 },
        };

        var json = Encoding.UTF8.GetString(IndexJson.Serialize(payload));

        Assert.Equal(
            "{\"specVersion\":1,\"type\":\"timestamp\",\"version\":3,\"expires\":\"2026-10-12T06:30:15Z\"," +
            $"\"snapshot\":{{\"version\":4,\"sha256\":\"{new string('a', 64)}\",\"length\":10}},\"moderation\":{{\"version\":2,\"sha256\":\"{new string('b', 64)}\",\"length\":20}}}}",
            json);
    }

    [Fact]
    public void RoundTripsAPluginRecordWithNumericImageKeysAndKebabCaseStates()
    {
        var record = Records.Plugin("lumen.todo", "Lumen Todo", PluginTier.Unverified, [Records.Version("1.0.0", dependencies: [("harbor.issues", ">=1.2.0")])]);

        var json = Encoding.UTF8.GetString(IndexJson.Serialize(record));
        var read = IndexJson.Deserialize<PluginRecord>(Encoding.UTF8.GetBytes(json));

        Assert.Contains("\"icon\":{\"64\":{", json, StringComparison.Ordinal);
        Assert.Contains("\"tier\":\"unverified\"", json, StringComparison.Ordinal);
        Assert.Contains("\"range\":\">=1.2.0 <2.0.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"runesmithApi\":\">=0.1.0 <0.2.0\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"banner\"", json, StringComparison.Ordinal);
        Assert.Equal(record.Versions[0].Version, read.Versions[0].Version);
        Assert.Equal(record.Versions[0].Dependencies[0].Range, read.Versions[0].Dependencies[0].Range);
        Assert.Equal(json, Encoding.UTF8.GetString(IndexJson.Serialize(read)));
    }

    [Fact]
    public void IgnoresUnknownPropertiesInIndexFiles()
    {
        const string json = """{"specVersion":1,"type":"publishers","version":1,"future":true,"publishers":[]}""";

        Assert.Empty(IndexJson.Deserialize<PublisherList>(Encoding.UTF8.GetBytes(json)).Publishers);
    }

    [Fact]
    public void RejectsANewerSpecVersion()
    {
        const string json = """{"specVersion":2,"type":"publishers","version":1,"publishers":[]}""";

        var exception = Assert.Throws<InvalidDataException>(() => IndexJson.Deserialize<PublisherList>(Encoding.UTF8.GetBytes(json)));
        Assert.Contains("specVersion 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAMissingRequiredField()
    {
        const string json = """{"specVersion":1,"type":"publishers"}""";

        Assert.Throws<InvalidDataException>(() => IndexJson.Deserialize<PublisherList>(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void RejectsTimesThatAreNotUtcWithAZ()
    {
        const string json = """{"specVersion":1,"type":"moderation","version":1,"expires":"2026-10-12T06:30:15+02:00","entries":[]}""";

        Assert.Throws<InvalidDataException>(() => IndexJson.Deserialize<ModerationPayload>(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void WritesThePolicyWithAnExplicitNullOldestRunesmith()
    {
        var policy = new PolicyPayload
        {
            Type = PayloadTypes.Policy,
            Version = 1,
            Categories = ["Other"],
            Capabilities = new Dictionary<string, CapabilityLabel> { ["network"] = new() { Label = "Network" } },
            OldestRunesmith = null,
            Mirrors = [],
            HoldHours = 24,
        };

        var json = Encoding.UTF8.GetString(IndexJson.Serialize(policy));

        Assert.Contains("\"oldestRunesmith\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"capabilities\":{\"network\":{\"label\":\"Network\"}}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesEnvelopesWithBase64AndKeyid()
    {
        var envelope = new DsseEnvelope
        {
            PayloadType = DsseEnvelope.HubPayloadType,
            Payload = "{}"u8.ToArray(),
            Signatures = [new DsseSignature { KeyId = "ab", Sig = new byte[] { 1, 2, 3 } }],
        };

        var json = Encoding.UTF8.GetString(IndexJson.Serialize(envelope));

        Assert.Equal("""{"payloadType":"application/vnd.runesmith.hub+json","payload":"e30=","signatures":[{"keyid":"ab","sig":"AQID"}]}""", json);
    }

    [Fact]
    public void WritesOwnerTypesInGithubCasingAndModerationReasonsInKebabCase()
    {
        var entry = new RegistryModerationEntry
        {
            Id = 7,
            Target = new ModerationTarget { Kind = ModerationTargetKind.Versions, Plugin = "harbor.issues", Range = VersionRange.Parse("1.3.1") },
            Action = ModerationAction.Block,
            Reason = ModerationReason.CompromisedAccount,
            Note = "Compromised.",
            CreatedAt = Records.Time,
            CreatedBy = 42,
        };
        var publisher = new PublisherFile
        {
            Name = "harbor",
            DisplayName = "Harbor Collective",
            Owner = new PublisherOwner { Id = 5, Login = "harbor", Type = OwnerType.Organization },
            State = PublisherState.Active,
        };

        var entryJson = RegistryJson.SerializeToString(entry);
        var publisherJson = RegistryJson.SerializeToString(publisher);

        Assert.Contains("\"reason\": \"compromised-account\"", entryJson, StringComparison.Ordinal);
        Assert.Contains("\"range\": \"1.3.1\"", entryJson, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"Organization\"", publisherJson, StringComparison.Ordinal);
        Assert.Equal("00007.json", RegistryModerationEntry.FileName(7));
    }

    [Fact]
    public void WritesRegistryFilesWithTwoSpaceIndentationLfAndAFinalNewline()
    {
        var json = RegistryJson.SerializeToString(new RegistryPolicy { Mirrors = ["https://mirror.example/"] });

        Assert.Equal("{\n  \"mirrors\": [\n    \"https://mirror.example/\"\n  ],\n  \"holdHours\": 24\n}\n", json);
    }

    [Fact]
    public void RejectsUnknownPropertiesInRegistryFiles()
    {
        const string json = """{ "mirrors": [], "holdHours": 24, "holdHour": 1 }""";

        Assert.Throws<JsonException>(() => RegistryJson.Deserialize<RegistryPolicy>(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void ARegistryVersionRecordKeepsRegistryOnlyFieldsOutOfTheIndex()
    {
        var version = Records.Version("1.3.2");
        var registry = new RegistryVersionRecord
        {
            Version = version.Version,
            State = version.State,
            Review = version.Review,
            Commit = version.Commit,
            Tag = version.Tag,
            ReleaseAuthor = version.ReleaseAuthor,
            Package = version.Package,
            Contracts = version.Contracts,
            RunesmithApi = version.RunesmithApi,
            Dependencies = version.Dependencies,
            Capabilities = version.Capabilities,
            NetworkHosts = version.NetworkHosts,
            Platforms = version.Platforms,
            Analysis = version.Analysis,
            Build = version.Build,
            ReleaseNotes = version.ReleaseNotes,
            PublishedAt = version.PublishedAt,
            Summary = version.Summary,
            Name = version.Name,
            Categories = version.Categories,
            Keywords = version.Keywords,
            LanguagePack = DutchPack,
            TrackingIssue = 12,
            BuildRun = 99,
            AnalysisDigest = new string('e', 64),
        };

        var registryJson = RegistryJson.SerializeToString(registry);
        var read = RegistryJson.Deserialize<RegistryVersionRecord>(Encoding.UTF8.GetBytes(registryJson));
        var indexRecord = read.ToIndexRecord();

        Assert.EndsWith("\"analysisDigest\": \"" + new string('e', 64) + "\"\n}\n", registryJson, StringComparison.Ordinal);
        Assert.Equal(12, read.TrackingIssue);
        Assert.Equal(typeof(VersionRecord), indexRecord.GetType());
        Assert.Equivalent(DutchPack, indexRecord.LanguagePack, strict: true);
        Assert.DoesNotContain("trackingIssue", Encoding.UTF8.GetString(IndexJson.Serialize(Records.Plugin("ab.cd", "A", PluginTier.Official, [indexRecord]))), StringComparison.Ordinal);
    }

    private static readonly LanguagePackSummary DutchPack = new()
    {
        Language = "nl",
        Name = "Nederlands",
        EnglishName = "Dutch",
        Targets = ["runesmith", "runesmith.git"],
    };

    [Fact]
    public void CarriesTheLanguagesAndLanguagePackOfAVersionAndLeavesThemOutOfOtherVersions()
    {
        var localized = Records.Version("1.0.1") with { Languages = ["en", "nl"] };
        var pack = Records.Version("1.0.2") with { LanguagePack = DutchPack };

        var json = Encoding.UTF8.GetString(IndexJson.Serialize(Records.Plugin("ab.cd", "A", PluginTier.Official, [pack, localized, Records.Version("1.0.0")])));
        var read = IndexJson.Deserialize<PluginRecord>(Encoding.UTF8.GetBytes(json));

        Assert.Contains("\"languagePack\":{\"language\":\"nl\",\"name\":\"Nederlands\",\"englishName\":\"Dutch\",\"targets\":[\"runesmith\",\"runesmith.git\"]}", json, StringComparison.Ordinal);
        Assert.Contains("\"languages\":[\"en\",\"nl\"]", json, StringComparison.Ordinal);
        Assert.Equal(1, json.Split("\"languages\"").Length - 1);
        Assert.Equal(1, json.Split("\"languagePack\"").Length - 1);
        Assert.Equivalent(DutchPack, read.Versions[0].LanguagePack, strict: true);
        Assert.Equal(["en", "nl"], read.Versions[1].Languages);
        Assert.Equal((null, null), (read.Versions[2].Languages, read.Versions[2].LanguagePack));
    }

    private static string Packaged(string localization)
    {
        var json = $$"""
            {
              "schemaVersion": 1, "id": "lumen.todo", "name": "Lumen Todo", "version": "1.0.0", "summary": "Todos.", "authors": ["Lumen"],
              "license": "MIT", "repository": "https://github.com/lumen/todo", "runesmithApi": "^0.1.0",
              "projects": { "contracts": "A.Contracts.csproj", "implementation": "A.csproj" }, "icon": "icon.png", "categories": ["Other"]{{localization}}
            }
            """;
        var assemblies = new PackagedAssemblies
        {
            Contracts = new AssemblyFile { File = "A.Contracts.dll", Sha256 = new string('a', 64) },
            Implementation = new AssemblyFile { File = "A.dll", Sha256 = new string('b', 64) },
        };
        var manifest = PackagedManifest.Create(
            ManifestValidator.Validate(Encoding.UTF8.GetBytes(json)).Manifest!, assemblies, new PackageBuild { Commit = new string('c', 40), Run = "local", Sdk = "10.0.400" });
        return Encoding.UTF8.GetString(IndexJson.Serialize(manifest));
    }

    [Fact]
    public void WritesTheLanguagesAndLanguagePackIntoThePackagedManifestOnlyWhenTheManifestHasThem()
    {
        var localized = Packaged(", \"languages\": [\"en\", \"nl\"]");
        var pack = Packaged(", \"languagePack\": { \"language\": \"nl\", \"name\": \"Nederlands\", \"englishName\": \"Dutch\" }");

        Assert.DoesNotContain("\"language", Packaged(""), StringComparison.Ordinal);
        Assert.Contains("\"languages\":[\"en\",\"nl\"],\"assemblies\"", localized, StringComparison.Ordinal);
        Assert.Contains("\"languagePack\":{\"language\":\"nl\",\"name\":\"Nederlands\",\"englishName\":\"Dutch\"},\"assemblies\"", pack, StringComparison.Ordinal);
        Assert.Equal(["en", "nl"], IndexJson.Deserialize<PackagedManifest>(Encoding.UTF8.GetBytes(localized)).Languages);
        Assert.Equal("Nederlands", IndexJson.Deserialize<PackagedManifest>(Encoding.UTF8.GetBytes(pack)).LanguagePack!.Name);
    }

    [Fact]
    public void ReadsALanguagePackWithoutTargetsAsTranslatingNothing()
    {
        var json = Encoding.UTF8.GetString(IndexJson.Serialize(Records.Plugin("ab.cd", "A", PluginTier.Official, [Records.Version("1.0.0") with { LanguagePack = DutchPack }])))
            .Replace(",\"targets\":[\"runesmith\",\"runesmith.git\"]", "", StringComparison.Ordinal);

        var pack = IndexJson.Deserialize<PluginRecord>(Encoding.UTF8.GetBytes(json)).Versions[0].LanguagePack!;

        Assert.Equal("Dutch", pack.EnglishName);
        Assert.Empty(pack.Targets);
    }

    [Fact]
    public void CarriesTheLicenseReportLinkAndYankReasonOfAVersionAndReadsRecordsWithoutThem()
    {
        var yanked = Records.Version("1.3.2", VersionState.Yanked) with
        {
            License = "MIT OR Apache-2.0",
            ReportUrl = "https://github.com/RunesmithHub/registry/issues/12",
            YankReason = "Corrupts settings.",
        };

        var json = Encoding.UTF8.GetString(IndexJson.Serialize(Records.Plugin("ab.cd", "A", PluginTier.Official, [yanked, Records.Version("1.3.1")])));
        var read = IndexJson.Deserialize<PluginRecord>(Encoding.UTF8.GetBytes(json));

        Assert.Contains("\"license\":\"MIT OR Apache-2.0\",\"reportUrl\":\"https://github.com/RunesmithHub/registry/issues/12\",\"yankReason\":\"Corrupts settings.\"", json, StringComparison.Ordinal);
        Assert.Equal(("MIT OR Apache-2.0", "https://github.com/RunesmithHub/registry/issues/12", "Corrupts settings."), (read.Versions[0].License, read.Versions[0].ReportUrl, read.Versions[0].YankReason));
        Assert.Equal((null, null, null), (read.Versions[1].License, read.Versions[1].ReportUrl, read.Versions[1].YankReason));
    }

    [Fact]
    public void ReadsReservedNamesWithComments()
    {
        var names = ReservedNames.Parse("# Reserved\nrunesmith\n  hub # the hub itself\n\nofficial\n");

        Assert.True(names.SetEquals(["runesmith", "hub", "official"]));
    }
}
