using RunesmithHub.Tool.Fixture;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Keys;

namespace RunesmithHub.Tool.Tests.Support;

/// <summary>Throwaway keys with a signed first root, and helpers to put the online keys into an environment.</summary>
internal sealed class TestKeys : IDisposable
{
    public TestKeys(DateTimeOffset now)
    {
        var template = new RootTemplate(1, now.AddDays(365),
            new RoleTemplate([.. Keys.Root.Select(key => key.PublicKey)], 2),
            new RoleTemplate([Keys.Publish.PublicKey], 1),
            new RoleTemplate([Keys.Moderation.PublicKey], 1),
            new RoleTemplate([Keys.Timestamp.PublicKey], 1));
        var root = RootCeremony.Create(template);
        root = RootCeremony.Sign(root, Keys.Root[0]).Envelope;
        Root = RootCeremony.Sign(root, Keys.Root[1]).Envelope;
    }

    public FixtureKeys Keys { get; } = new();

    /// <summary>Gets the first root envelope, signed by two of the three root keys.</summary>
    public byte[] Root { get; }

    /// <summary>Puts exactly the given roles' keys into the environment and removes the others.</summary>
    public void Use(Dictionary<string, string> environment, params OnlineRole[] roles)
    {
        foreach (var role in Enum.GetValues<OnlineRole>())
            environment.Remove(OnlineKeys.VariableFor(role));
        foreach (var role in roles)
        {
            environment[OnlineKeys.VariableFor(role)] = role switch
            {
                OnlineRole.Publish => Keys.Publish.ExportPem(),
                OnlineRole.Moderation => Keys.Moderation.ExportPem(),
                _ => Keys.Timestamp.ExportPem(),
            };
        }
    }

    public void Dispose() => Keys.Dispose();
}
