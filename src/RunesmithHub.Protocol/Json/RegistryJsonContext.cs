using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Registry;

namespace RunesmithHub.Protocol.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = true,
    IndentSize = 2,
    NewLine = "\n",
    Converters = [typeof(UtcTimestampConverter), typeof(SemanticVersionConverter), typeof(VersionRangeConverter)])]
[JsonSerializable(typeof(DeveloperManifest))]
[JsonSerializable(typeof(PublisherFile))]
[JsonSerializable(typeof(Registration))]
[JsonSerializable(typeof(RegistryVersionRecord))]
[JsonSerializable(typeof(RegistryModerationEntry))]
[JsonSerializable(typeof(RegistryPolicy))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
internal sealed partial class RegistryJsonContext : JsonSerializerContext;
