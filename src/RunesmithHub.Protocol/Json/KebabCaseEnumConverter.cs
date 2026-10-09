using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Json;

/// <summary>Reads and writes enum values as lowercase kebab-case names, such as <c>compromised-account</c>; numbers are rejected.</summary>
public sealed class KebabCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false)
    where TEnum : struct, Enum;
