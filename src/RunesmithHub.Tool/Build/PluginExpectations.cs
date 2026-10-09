using RunesmithHub.Protocol.Registry;

namespace RunesmithHub.Tool.Build;

/// <summary>What a plugin's manifest must match: the registration's ID and repository, and the version the release tag names.</summary>
internal sealed record PluginExpectations(string? Id, string? Repository, string? Version)
{
    /// <summary>Gets the expectations of a release: the registration's ID and repository, and the tag without its prefix.</summary>
    /// <exception cref="ToolException">The tag does not start with the registration's tag prefix.</exception>
    public static PluginExpectations ForRelease(Registration registration, string tag)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var prefix = registration.Repository.TagPrefix;
        if (!tag.StartsWith(prefix, StringComparison.Ordinal) || tag.Length == prefix.Length)
            throw new ToolException($"The tag \"{tag}\" does not start with \"{prefix}\" followed by a version.");
        return new PluginExpectations(registration.Id, registration.Repository.Url, tag[prefix.Length..]);
    }
}
