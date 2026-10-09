using System.Text.Json.Serialization;

namespace RunesmithHub.Protocol.Index;

/// <summary>A document that travels in a signed envelope and expires.</summary>
public abstract record SignedPayload : IndexDocument
{
    [JsonPropertyOrder(-7)]
    public required DateTimeOffset Expires { get; init; }
}
