using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Resolution;

/// <summary>Turns the catalog, the installed plugins, the settings and a request into the facts and preferences the solver works with.</summary>
internal sealed class ResolutionProblem : IPackageSource
{
    private const string Root = "";

    private readonly HubCatalog _catalog;
    private readonly ResolutionSettings _settings;
    private readonly IReadOnlyDictionary<string, InstalledPlugin> _installed;
    private readonly ResolutionRequest _request;
    private readonly IReadOnlySet<string> _roots;
    private readonly Dictionary<string, PackageVersions> _packages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Incompatibility>> _dependencyCache = new(StringComparer.Ordinal);
    private List<Incompatibility>? _rootDependencies;

    public ResolutionProblem(HubCatalog catalog, ResolutionSettings settings, IReadOnlyDictionary<string, InstalledPlugin> installed, ResolutionRequest request)
    {
        _catalog = catalog;
        _settings = settings;
        _installed = installed;
        _request = request;
        _roots = FindRoots();
    }

    public string RootPackage => Root;

    public ResolutionSettings Settings => _settings;

    public IReadOnlySet<string> Roots => _roots;

    public bool IsInstalled(string id) => _installed.ContainsKey(id);

    public PackageVersions Package(string id)
    {
        if (!_packages.TryGetValue(id, out var package))
            _packages[id] = package = new PackageVersions(id, _catalog.FindPlugin(id), _installed.GetValueOrDefault(id)?.Version);
        return package;
    }

    /// <summary>Gets the display name of a plugin: its record's name, or its ID when the index does not have it.</summary>
    public string NameOf(string id) => id == Root ? "" : Package(id).Record?.Name ?? id;

    public int VersionCount(string package) => package == Root ? 1 : Package(package).Versions.Count;

    public int Priority(string package) =>
        package == Root ? 0 : _installed.ContainsKey(package) && PrefersInstalled(package) ? 1 : 2;

    public int Choose(string package, VersionSet allowed)
    {
        if (package == Root)
            return 0;
        if (PrefersInstalled(package) && Package(package).InstalledIndex is { } installed && allowed.Contains(installed))
            return installed;
        return allowed.Indexes().Last();
    }

    /// <summary>
    /// Finds what the user wants installed: every requested plugin that is not being removed and still has a version that can be chosen,
    /// and the plugin the request installs or updates.
    /// </summary>
    private HashSet<string> FindRoots()
    {
        var roots = _installed.Values
            .Where(plugin => plugin.Requested && !_request.RemovedIds.Contains(plugin.Id, StringComparer.Ordinal) && HasAnyCandidate(plugin.Id))
            .Select(plugin => plugin.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (_request.Kind is RequestKind.Install or RequestKind.Update && _request.PluginId is { } target)
            roots.Add(target);
        return roots;
    }

    private bool PrefersInstalled(string package) => _request.Kind switch
    {
        RequestKind.UpdateAll => false,
        RequestKind.Update or RequestKind.Install => _request.PluginId != package,
        _ => true,
    };

    public IEnumerable<Incompatibility> Facts(string package)
    {
        if (package == Root)
            yield break;

        var versions = Package(package);
        foreach (var group in Enumerable.Range(0, versions.Versions.Count)
            .Select(index => (Index: index, Reason: Unavailable(versions, index)))
            .Where(pair => pair.Reason is not null)
            .GroupBy(pair => pair.Reason!.Value))
        {
            var set = VersionSet.Of(versions.Versions.Count, index => group.Any(pair => pair.Index == index));
            yield return new Incompatibility([new Term(package, set, IsPositive: true)], new UnavailableCause(package, group.Key), Root);
        }
    }

    /// <summary>Gets why a version can't be chosen whatever else is selected, or null when it can be.</summary>
    public Unavailability? Unavailable(PackageVersions package, int index)
    {
        var record = package.Record!;
        var version = package.Versions[index];
        var state = _catalog.GetEffectiveState(record, version).State;
        if (state == VersionState.Blocked)
            return Unavailability.Blocked;
        if (!_settings.AllowedTiers.Allows(record.Tier))
            return Unavailability.TierSetting;
        if (!_settings.Host.SupportsApi(version.RunesmithApi))
            return Unavailability.Api;
        if (!_settings.Host.SupportsPlatform(version))
            return Unavailability.Platform;
        if (state == VersionState.Published || package.InstalledIndex == index)
            return null;
        if (state == VersionState.Yanked && _request is { Kind: RequestKind.Install } && _request.PluginId == package.Id && _request.Version == version.Version)
            return null;
        return UnavailabilityExtensions.FromState(state);
    }

    /// <summary>Gets whether a plugin has any version that could be chosen at all.</summary>
    public bool HasAnyCandidate(string id)
    {
        var package = Package(id);
        return package.Record is not null && Enumerable.Range(0, package.Versions.Count).Any(index => Unavailable(package, index) is null);
    }

    public IEnumerable<Incompatibility> Dependencies(string package, int version) =>
        package == Root ? _rootDependencies ??= [.. RootDependencies()] : VersionDependencies(Package(package), version);

    private IEnumerable<Incompatibility> RootDependencies()
    {
        var rootTerm = new Term(Root, VersionSet.Full(1), IsPositive: true);
        foreach (var id in _roots.Order(StringComparer.Ordinal))
        {
            var package = Package(id);
            var (kind, set) = RequirementFor(package);
            yield return new Incompatibility([rootTerm, new Term(id, set, IsPositive: false)], new RequirementCause(id, kind, _request.PluginId == id ? _request.Version : null), Root);
        }

        foreach (var id in _request.RemovedIds.Order(StringComparer.Ordinal))
        {
            var size = Package(id).Versions.Count;
            yield return new Incompatibility([rootTerm, new Term(id, VersionSet.Full(size), IsPositive: true)], new RemovalCause(id), Root);
        }
    }

    private (RequirementKind Kind, VersionSet Set) RequirementFor(PackageVersions package)
    {
        var size = package.Versions.Count;
        if (_request.PluginId == package.Id && _request.Kind == RequestKind.Install && _request.Version is { } exact)
            return (RequirementKind.Install, VersionSet.Of(size, index => package.Versions[index].Version == exact));

        var kind = _request.PluginId == package.Id
            ? _request.Kind == RequestKind.Install ? RequirementKind.Install : RequirementKind.Update
            : RequirementKind.Installed;
        return (kind, VersionSet.Of(size, index => !package.Versions[index].Version.IsPreRelease || AllowsPreRelease(package, index)));
    }

    private bool AllowsPreRelease(PackageVersions package, int index) =>
        _settings.PreReleaseOptIns.Contains(package.Id) || package.InstalledIndex == index;

    /// <summary>Gets the versions of a dependency a range allows, before availability.</summary>
    public VersionSet RangeSet(PackageVersions package, VersionRange range) =>
        VersionSet.Of(package.Versions.Count, index => range.Contains(package.Versions[index].Version, AllowsPreRelease(package, index)));

    private List<Incompatibility> VersionDependencies(PackageVersions dependent, int version)
    {
        var result = new List<Incompatibility>();
        foreach (var dependency in dependent.Versions[version].Dependencies)
        {
            var key = $"{dependent.Id} {dependency.Id} {dependency.Range}";
            if (!_dependencyCache.TryGetValue(key, out var cached))
                _dependencyCache[key] = cached = [.. DependencyIncompatibilities(dependent, dependency)];
            result.AddRange(cached);
        }

        return result;
    }

    private IEnumerable<Incompatibility> DependencyIncompatibilities(PackageVersions dependent, PluginDependency dependency)
    {
        var sharing = VersionSet.Of(dependent.Versions.Count, index =>
            dependent.Versions[index].Dependencies.Any(other => other.Id == dependency.Id && other.Range.Equals(dependency.Range)));
        var dependentTerm = new Term(dependent.Id, sharing, IsPositive: true);
        var target = Package(dependency.Id);
        var allowed = RangeSet(target, dependency.Range);
        var cause = new DependencyCause(dependent.Id, dependency.Id, dependency.Range);
        if (allowed.IsEmpty)
        {
            yield return new Incompatibility([dependentTerm], cause, Root);
            yield break;
        }

        yield return new Incompatibility([dependentTerm, new Term(dependency.Id, allowed, IsPositive: false)], cause, Root);

        var dependentTier = dependent.Record!.Tier;
        var targetTier = target.Record!.Tier;
        var tierAllowed = TrustRules.MayDependOn(dependentTier, targetTier);
        var untrusted = tierAllowed
            ? allowed.Intersect(VersionSet.Of(target.Versions.Count, index => !TrustRules.AcceptsVersion(dependentTier, target.Versions[index])))
            : allowed;
        if (!untrusted.IsEmpty)
        {
            var problem = tierAllowed ? TrustProblem.NotReviewed : TrustProblem.Tier;
            yield return new Incompatibility([dependentTerm, new Term(dependency.Id, untrusted, IsPositive: true)], new TrustCause(dependent.Id, dependency.Id, problem), Root);
        }
    }
}
