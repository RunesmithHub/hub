using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Signing;

namespace RunesmithHub.Tool.IndexBuild;

/// <summary>The registry's chain of signed roots, verified from the first: the trust root every index build starts from.</summary>
internal sealed class TrustRoot
{
    private TrustRoot(IReadOnlyList<byte[]> envelopes, RootPayload root)
    {
        Envelopes = envelopes;
        Root = root;
    }

    /// <summary>Gets every root envelope, version 1 first.</summary>
    public IReadOnlyList<byte[]> Envelopes { get; }

    /// <summary>Gets the newest root.</summary>
    public RootPayload Root { get; }

    /// <exception cref="ToolException">There is no root, or the chain does not verify.</exception>
    public static TrustRoot Verify(IReadOnlyList<byte[]> envelopes)
    {
        if (envelopes.Count == 0)
            throw new ToolException("The registry has no keys/root/1.json.");
        try
        {
            var root = RootChain.VerifyFirst(envelopes[0]);
            foreach (var next in envelopes.Skip(1))
                root = RootChain.VerifyNext(root, next);
            return new TrustRoot(envelopes, root);
        }
        catch (InvalidDataException exception)
        {
            throw new ToolException($"The roots in keys/root do not verify: {exception.Message}", exception);
        }
    }

    /// <summary>Gets whether an envelope is signed by enough of a role's keys in the newest root.</summary>
    public bool IsSignedBy(DsseEnvelope envelope, RoleKeys role) => RoleVerifier.MeetsThreshold(envelope, Root, role);
}
