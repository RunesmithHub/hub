namespace RunesmithHub.Protocol.Updating;

/// <summary>Keeps the trusted index between runs.</summary>
public interface ITrustStore
{
    /// <summary>Loads what was saved last, or null when nothing was saved.</summary>
    TrustedIndex? Load();

    /// <summary>Replaces what is saved in one step: a failure part way leaves the previous contents.</summary>
    void Save(TrustedIndex index);
}
