using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Index;

/// <summary>The fields every index document starts with.</summary>
public abstract record IndexDocument
{
    /// <summary>The newest <c>specVersion</c> this library reads.</summary>
    public const int SupportedSpecVersion = 1;

    [JsonPropertyOrder(-10)]
    public int SpecVersion { get; init; } = SupportedSpecVersion;

    /// <summary>Gets the document type, such as <c>root</c>; see <see cref="PayloadTypes"/>.</summary>
    [JsonPropertyOrder(-9)]
    public required string Type { get; init; }

    /// <summary>Gets the document's version, which starts at 1 and only goes up.</summary>
    [JsonPropertyOrder(-8)]
    public required int Version { get; init; }
}
