namespace RunesmithHub.Protocol.Updating;

/// <summary>Keeps the trusted index in memory, for tests and short-lived tools.</summary>
public sealed class InMemoryTrustStore(TrustedIndex? initial = null) : ITrustStore
{
    private TrustedIndex? _index = initial;

    /// <summary>Gets how many times <see cref="Save"/> was called.</summary>
    public int SaveCount { get; private set; }

    public TrustedIndex? Load() => _index;

    public void Save(TrustedIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        _index = index;
        SaveCount++;
    }
}
