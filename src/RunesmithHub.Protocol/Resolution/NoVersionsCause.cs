namespace RunesmithHub.Protocol.Resolution;

/// <summary>No version of a package is left in the allowed set.</summary>
internal sealed record NoVersionsCause(string Package) : IncompatibilityCause;
