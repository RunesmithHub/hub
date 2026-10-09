using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Tool.Analysis.Api;

/// <summary>How a visible member differs between two versions.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<ApiChangeKind>))]
internal enum ApiChangeKind
{
    Removed,
    Changed,
    Added,
}

/// <summary>One difference between two versions' public APIs, with the signatures before and after.</summary>
internal sealed record ApiChange(ApiChangeKind Kind, string Member, string? Before, string? After);
