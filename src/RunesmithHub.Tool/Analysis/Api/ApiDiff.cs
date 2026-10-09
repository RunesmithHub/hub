namespace RunesmithHub.Tool.Analysis.Api;

/// <summary>
/// The differences between two versions' contracts: removed and changed members break dependents, and so does a new abstract member of an
/// existing type others implement; other new members are additions.
/// </summary>
internal sealed record ApiDiff(IReadOnlyList<ApiChange> Breaking, IReadOnlyList<ApiChange> Additions)
{
    public static ApiDiff Compare(ApiSurface previous, ApiSurface current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var breaking = new List<ApiChange>();
        var additions = new List<ApiChange>();
        foreach (var (key, before) in previous.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!current.Entries.TryGetValue(key, out var after))
                breaking.Add(new ApiChange(ApiChangeKind.Removed, key, before.Signature, null));
            else if (!string.Equals(before.Signature, after.Signature, StringComparison.Ordinal))
                breaking.Add(new ApiChange(ApiChangeKind.Changed, key, before.Signature, after.Signature));
        }

        foreach (var (key, after) in current.Entries.Where(entry => !previous.Entries.ContainsKey(entry.Key)).OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var change = new ApiChange(ApiChangeKind.Added, key, null, after.Signature);
            (after.AddsObligation && previous.Entries.ContainsKey(TypeOf(key)) ? breaking : additions).Add(change);
        }

        return new ApiDiff(breaking, additions);
    }

    private static string TypeOf(string memberKey)
    {
        var parameters = memberKey.IndexOf('(', StringComparison.Ordinal);
        var name = parameters < 0 ? memberKey : memberKey[..parameters];
        var indexer = name.IndexOf('[', StringComparison.Ordinal);
        name = indexer < 0 ? name : name[..indexer];
        return name[..name.LastIndexOf('.')];
    }
}
