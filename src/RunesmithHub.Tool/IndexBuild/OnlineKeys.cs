using System.Security.Cryptography;
using RunesmithHub.Protocol.Signing;

namespace RunesmithHub.Tool.IndexBuild;

/// <summary>An online role and the environment variable its key comes from.</summary>
internal enum OnlineRole
{
    Publish,
    Moderation,
    Timestamp,
}

/// <summary>The online keys a signing job holds: each read from its own environment variable as unencrypted PKCS#8 PEM.</summary>
internal sealed class OnlineKeys : IDisposable
{
    private readonly Dictionary<OnlineRole, EcdsaSigner> _keys;

    private OnlineKeys(Dictionary<OnlineRole, EcdsaSigner> keys) => _keys = keys;

    public static string VariableFor(OnlineRole role) => role switch
    {
        OnlineRole.Publish => "HUB_PUBLISH_KEY",
        OnlineRole.Moderation => "HUB_MODERATION_KEY",
        _ => "HUB_TIMESTAMP_KEY",
    };

    /// <exception cref="ToolException">A key variable is set but does not hold a P-256 private key.</exception>
    public static OnlineKeys FromEnvironment(Func<string, string?> environment)
    {
        var keys = new Dictionary<OnlineRole, EcdsaSigner>();
        foreach (var role in Enum.GetValues<OnlineRole>())
        {
            if (environment(VariableFor(role)) is not { Length: > 0 } pem)
                continue;
            try
            {
                keys[role] = EcdsaSigner.FromPem(pem);
            }
            catch (Exception exception) when (exception is CryptographicException or ArgumentException)
            {
                foreach (var key in keys.Values)
                    key.Dispose();
                throw new ToolException($"{VariableFor(role)} does not hold an unencrypted PKCS#8 PEM P-256 private key.", exception);
            }
        }

        return new OnlineKeys(keys);
    }

    public EcdsaSigner? Find(OnlineRole role) => _keys.GetValueOrDefault(role);

    /// <exception cref="ToolException">The key is not set.</exception>
    public EcdsaSigner Require(OnlineRole role) => Find(role) ?? throw new ToolException($"The environment variable {VariableFor(role)} is not set.");

    public void Dispose()
    {
        foreach (var key in _keys.Values)
            key.Dispose();
    }
}
