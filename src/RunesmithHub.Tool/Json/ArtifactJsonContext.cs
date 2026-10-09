using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Tool.Analysis.Heuristics;
using RunesmithHub.Tool.Analysis.Reporting;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Findings;
using RunesmithHub.Tool.Images;

namespace RunesmithHub.Tool.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    WriteIndented = true,
    IndentSize = 2,
    NewLine = "\n",
    Converters = [typeof(UtcTimestampConverter), typeof(SemanticVersionConverter), typeof(VersionRangeConverter)])]
[JsonSerializable(typeof(BuildPlan))]
[JsonSerializable(typeof(HashList))]
[JsonSerializable(typeof(ImageList))]
[JsonSerializable(typeof(AnalysisReport))]
[JsonSerializable(typeof(PrivateFindings))]
[JsonSerializable(typeof(SealedFindings))]
[JsonSerializable(typeof(HeuristicRuleSet))]
internal sealed partial class ArtifactJsonContext : JsonSerializerContext;
