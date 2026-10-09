using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Tool.Dashboard;
using RunesmithHub.Tool.Discovery;
using RunesmithHub.Tool.Fixture;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Versions;

namespace RunesmithHub.Tool.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(UtcTimestampConverter)])]
[JsonSerializable(typeof(DashboardSummary))]
[JsonSerializable(typeof(InstallStats))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, CachedResponse>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(IReadOnlyList<Publication>))]
[JsonSerializable(typeof(BuildJob))]
[JsonSerializable(typeof(BuildMatrix))]
internal sealed partial class ToolJsonContext : JsonSerializerContext;
