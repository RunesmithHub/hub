using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Signing;

namespace RunesmithHub.Protocol.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    Converters = [typeof(UtcTimestampConverter), typeof(SemanticVersionConverter), typeof(VersionRangeConverter)])]
[JsonSerializable(typeof(DsseEnvelope))]
[JsonSerializable(typeof(RootPayload))]
[JsonSerializable(typeof(TimestampPayload))]
[JsonSerializable(typeof(SnapshotPayload))]
[JsonSerializable(typeof(ModerationPayload))]
[JsonSerializable(typeof(PluginRecord))]
[JsonSerializable(typeof(PublisherList))]
[JsonSerializable(typeof(PolicyPayload))]
[JsonSerializable(typeof(PackagedManifest))]
[JsonSerializable(typeof(PublicKey))]
internal sealed partial class IndexJsonContext : JsonSerializerContext;
