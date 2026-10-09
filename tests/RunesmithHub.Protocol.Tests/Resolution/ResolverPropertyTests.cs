using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Resolution;
using RunesmithHub.Protocol.Tests.Support;
using RunesmithHub.Protocol.Versioning;

namespace RunesmithHub.Protocol.Tests.Resolution;

/// <summary>Checks the solver against an exhaustive search over small random hubs.</summary>
public sealed class ResolverPropertyTests
{
    private static readonly RunesmithHost Host = new(SemanticVersion.Parse("0.1.0"), SemanticVersion.Parse("0.1.0"), null);
    private static readonly string[] Ids = ["pkg.alpha", "pkg.bravo", "pkg.charlie", "pkg.delta", "pkg.echo"];
    private static readonly string[] Ranges = ["^1.0.0", "^2.0.0", ">=1.1.0 <2.1.0", "1.0.0", ">=2.0.0 <3.0.0", "^1.1.0 || ^2.1.0"];
    private static readonly string[] VersionPool = ["1.0.0", "1.1.0", "2.0.0", "2.1.0"];

    public static TheoryData<int> Seeds => [.. Enumerable.Range(1, 400)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void FindsAPlanExactlyWhenOneExistsAndThePlanIsValid(int seed)
    {
        var random = new Random(seed);
        var plugins = Ids.Select(id => Records.Plugin(id, id, PluginTier.Unverified, VersionPool
            .Where(_ => random.NextDouble() < 0.75)
            .Select(version => Records.Version(
                version,
                random.NextDouble() < 0.15 ? VersionState.Blocked : VersionState.Published,
                ReviewState.Automated,
                dependencies: Ids.Where(other => other != id && random.NextDouble() < 0.3).Select(other => (other, Ranges[random.Next(Ranges.Length)])).ToList())))).ToList();
        var target = Ids[random.Next(Ids.Length)];
        if (plugins.Single(p => p.Id == target).Versions.Count == 0)
            return;

        var catalog = Records.Catalog(plugins);
        var result = new Resolver(catalog, new ResolutionSettings(Host)).Resolve([], ResolutionRequest.Install(target));
        var exists = Search(plugins, selection => IsValid(plugins, catalog, target, selection));

        Assert.True(exists == result.Succeeded, $"Seed {seed}: exhaustive search says {(exists ? "a plan exists" : "no plan exists")}; the resolver said {result.Failure?.Message ?? "it found one"}.");
        if (result.Succeeded)
            AssertValid(plugins, catalog, target, result.Plan.Operations.ToDictionary(op => op.PluginId, op => op.To!));
        else
            Assert.NotEmpty(result.Failure.Message);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void KeepsWhatIsInstalledWhenThatIsValidAndOtherwiseAgreesWithExhaustiveSearch(int seed)
    {
        var random = new Random(seed);
        var plugins = Ids.Select(id => Records.Plugin(id, id, PluginTier.Unverified, VersionPool
            .Where(_ => random.NextDouble() < 0.75)
            .Select(version => Records.Version(
                version,
                random.NextDouble() switch { < 0.1 => VersionState.Blocked, < 0.2 => VersionState.Frozen, _ => VersionState.Published },
                ReviewState.Automated,
                dependencies: Ids.Where(other => other != id && random.NextDouble() < 0.25).Select(other => (other, Ranges[random.Next(Ranges.Length)])).ToList())))).ToList();
        var installed = plugins.Where(p => p.Versions.Count > 0 && random.NextDouble() < 0.5)
            .Select(p => new InstalledPlugin(p.Id, p.Versions[random.Next(p.Versions.Count)].Version, Requested: random.NextDouble() < 0.7))
            .ToList();
        var catalog = Records.Catalog(plugins);

        var result = new Resolver(catalog, new ResolutionSettings(Host)).Resolve(installed, ResolutionRequest.Sync());
        var roots = installed.Where(p => p.Requested && plugins.Single(r => r.Id == p.Id).Versions.Any(v => Usable(catalog, plugins, installed, p.Id, v.Version))).Select(p => p.Id).ToList();
        var exists = Search(plugins, selection => roots.All(selection.ContainsKey) && selection.All(pair => Usable(catalog, plugins, installed, pair.Key, pair.Value)) && DependenciesHold(plugins, selection));

        Assert.True(exists == result.Succeeded, $"Seed {seed}: exhaustive search says {(exists ? "a plan exists" : "no plan exists")}; the resolver said {result.Failure?.Message ?? "it found one"}.");
        if (!result.Succeeded)
            return;
        var selection = result.Plan.Operations.Where(op => op.To is not null).ToDictionary(op => op.PluginId, op => op.To!);
        Assert.True(roots.All(selection.ContainsKey) && selection.All(pair => Usable(catalog, plugins, installed, pair.Key, pair.Value)) && DependenciesHold(plugins, selection), $"Seed {seed}: the plan breaks a rule.");
        var keepAll = installed.Where(p => Usable(catalog, plugins, installed, p.Id, p.Version)).ToDictionary(p => p.Id, p => p.Version);
        if (keepAll.Count == installed.Count && roots.All(keepAll.ContainsKey) && DependenciesHold(plugins, keepAll) && installed.All(p => p.Requested))
            Assert.Empty(result.Plan.Changes);
    }

    private static bool Usable(HubCatalog catalog, List<PluginRecord> plugins, List<InstalledPlugin> installed, string id, SemanticVersion version)
    {
        var plugin = plugins.Single(p => p.Id == id);
        var state = catalog.GetEffectiveState(plugin, plugin.FindVersion(version)!).State;
        return state == VersionState.Published || (state != VersionState.Blocked && installed.Any(p => p.Id == id && p.Version == version));
    }

    private static bool DependenciesHold(List<PluginRecord> plugins, Dictionary<string, SemanticVersion> selection) =>
        selection.All(pair => plugins.Single(p => p.Id == pair.Key).FindVersion(pair.Value)!.Dependencies
            .All(d => selection.TryGetValue(d.Id, out var chosen) && d.Range.Contains(chosen)));

    private static bool Search(List<PluginRecord> plugins, Func<Dictionary<string, SemanticVersion>, bool> isValid)
    {
        var choices = plugins.Select(p => new SemanticVersion?[] { null }.Concat(p.Versions.Select(v => (SemanticVersion?)v.Version)).ToArray()).ToList();
        var indexes = new int[plugins.Count];
        while (true)
        {
            var selection = new Dictionary<string, SemanticVersion>();
            for (var i = 0; i < plugins.Count; i++)
            {
                if (choices[i][indexes[i]] is { } version)
                    selection[plugins[i].Id] = version;
            }

            if (isValid(selection))
                return true;

            var position = 0;
            while (position < plugins.Count && ++indexes[position] == choices[position].Length)
                indexes[position++] = 0;
            if (position == plugins.Count)
                return false;
        }
    }

    private static bool IsValid(List<PluginRecord> plugins, HubCatalog catalog, string target, Dictionary<string, SemanticVersion> selection)
    {
        if (!selection.ContainsKey(target))
            return false;
        foreach (var (id, version) in selection)
        {
            var plugin = plugins.Single(p => p.Id == id);
            var record = plugin.FindVersion(version)!;
            if (!catalog.GetEffectiveState(plugin, record).IsInstallable)
                return false;
            foreach (var dependency in record.Dependencies)
            {
                if (!selection.TryGetValue(dependency.Id, out var chosen) || !dependency.Range.Contains(chosen))
                    return false;
            }
        }

        return true;
    }

    private static void AssertValid(List<PluginRecord> plugins, HubCatalog catalog, string target, Dictionary<string, SemanticVersion> selection)
    {
        Assert.True(IsValid(plugins, catalog, target, selection), "The plan breaks a rule.");
        var needed = new HashSet<string> { target };
        var queue = new Queue<string>([target]);
        while (queue.Count > 0)
        {
            var record = plugins.Single(p => p.Id == queue.Peek()).FindVersion(selection[queue.Dequeue()])!;
            foreach (var dependency in record.Dependencies.Where(d => needed.Add(d.Id)))
                queue.Enqueue(dependency.Id);
        }

        Assert.True(needed.SetEquals(selection.Keys), "The plan installs plugins nothing needs.");
    }
}
