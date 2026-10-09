namespace RunesmithHub.Tool.Analysis.Detection;

/// <summary>The capabilities found in a package, each with where it was found.</summary>
internal sealed class DetectedCapabilities
{
    private readonly SortedDictionary<string, List<CallSite>> _sites = new(StringComparer.Ordinal);

    /// <summary>Gets the detected capability IDs in the order users see capabilities.</summary>
    public IReadOnlyList<string> Ids => [.. Protocol.Capabilities.All.Where(_sites.ContainsKey)];

    public bool Contains(string capability) => _sites.ContainsKey(capability);

    /// <summary>Gets every distinct place a capability is used, sorted.</summary>
    public IReadOnlyList<CallSite> SitesOf(string capability) =>
        _sites.TryGetValue(capability, out var sites)
            ? [.. sites.Distinct().OrderBy(site => site.Assembly, StringComparer.Ordinal).ThenBy(site => site.Member, StringComparer.Ordinal).ThenBy(site => site.Api, StringComparer.Ordinal)]
            : [];

    public void Add(string capability, CallSite site)
    {
        if (!_sites.TryGetValue(capability, out var sites))
            _sites[capability] = sites = [];
        sites.Add(site);
    }
}
