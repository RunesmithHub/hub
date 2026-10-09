using System.Text.Json.Serialization;
using RunesmithHub.Protocol.Json;

namespace RunesmithHub.Tool.Analysis.Heuristics;

/// <summary>What a heuristic rule looks at.</summary>
[JsonConverter(typeof(KebabCaseEnumConverter<HeuristicKind>))]
internal enum HeuristicKind
{
    /// <summary>String literals matching <see cref="HeuristicRule.Pattern"/>.</summary>
    String,

    /// <summary>Embedded resources whose name matches <see cref="HeuristicRule.Pattern"/>, if given, and that have at least <see cref="HeuristicRule.MinBytes"/>.</summary>
    Resource,

    /// <summary>String literals, resources and package files of at least <see cref="HeuristicRule.MinLength"/> bytes with at least <see cref="HeuristicRule.MinBitsPerByte"/> of entropy.</summary>
    Entropy,

    /// <summary>Resources and package files starting with one of the hex <see cref="HeuristicRule.Signatures"/>.</summary>
    Magic,

    /// <summary>Attributes or types with one of the <see cref="HeuristicRule.Names"/>.</summary>
    Attribute,

    /// <summary>An assembly that references every one of the <see cref="HeuristicRule.Apis"/>; <c>Type.*</c> or <c>Namespace.*</c> match prefixes.</summary>
    ApiCombination,
}
