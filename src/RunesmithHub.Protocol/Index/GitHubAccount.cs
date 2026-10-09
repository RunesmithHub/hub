namespace RunesmithHub.Protocol.Index;

/// <summary>A GitHub account by its permanent numeric ID and its current login.</summary>
public sealed record GitHubAccount
{
    public required long Id { get; init; }

    public required string Login { get; init; }
}
