using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;

namespace RunesmithHub.Tool.Tests.Support;

internal static class TestRegistrations
{
    public static Registration For(string id, string repository, PluginTier tier = PluginTier.Unverified) => new()
    {
        Id = id,
        Repository = new RegisteredRepository { Id = 1, Url = repository },
        Maintainers = [new GitHubAccount { Id = 2, Login = "lumen" }],
        Tier = tier,
        State = RegistrationState.Registered,
        RegisteredAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
    };
}
