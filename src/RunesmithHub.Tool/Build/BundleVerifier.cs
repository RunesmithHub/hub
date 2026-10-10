using RunesmithHub.Tool.NuGet;

namespace RunesmithHub.Tool.Build;

/// <summary>
/// Checks that every bundled file from a NuGet package is byte for byte the file in the package the lock file pins: the package is fetched
/// from the feed its source mapping assigns and must have the content hash the lock file records.
/// </summary>
internal sealed class BundleVerifier(INuGetPackageSource packages)
{
    /// <summary>Verifies the NuGet-sourced items, adding a problem for each mismatch, and returns the packages they came from, sorted by ID.</summary>
    public async Task<IReadOnlyList<BundledPackage>> VerifyAsync(
        IReadOnlyList<BundleItem> items, IReadOnlyList<LockedPackage> locked, List<string> problems, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(problems);
        var verified = new List<BundledPackage>();
        foreach (var group in items.Where(item => item.PackageId is not null).GroupBy(item => (Id: item.PackageId!, Version: item.PackageVersion!)))
        {
            var (id, version) = group.Key;
            var pinned = locked.FirstOrDefault(package => string.Equals(package.Id, id, StringComparison.OrdinalIgnoreCase) && string.Equals(package.Version, version, StringComparison.OrdinalIgnoreCase));
            if (pinned is null)
            {
                problems.Add($"{id} {version} is not in the lock files.");
                continue;
            }

            using var package = NuGetPackage.Open(await packages.GetAsync(id, version, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(package.ContentHash, pinned.ContentHash, StringComparison.Ordinal))
            {
                problems.Add($"{id} {version} from the feed does not have the content hash its lock file records.");
                continue;
            }

            foreach (var item in group)
            {
                var expected = package.ReadFile(item.PathInPackage!);
                var actual = CheckoutPaths.ReadFile(Path.GetDirectoryName(item.SourceFile)!, item.SourceFile);
                if (expected is null || !expected.AsSpan().SequenceEqual(actual))
                    problems.Add($"{(item.IsNative ? item.FileName : item.LibPath)} in the build output differs from {item.PathInPackage} in {id} {version}.");
            }

            verified.Add(BundledPackage.From(pinned, package));
        }

        return [.. verified.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)];
    }
}
