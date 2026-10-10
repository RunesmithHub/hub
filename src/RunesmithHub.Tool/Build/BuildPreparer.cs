using RunesmithHub.Protocol.Versioning;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.NuGet;
using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Build;

/// <summary>
/// Checks a plugin's source at the release commit before anything of it runs, and writes the NuGet configuration and MSBuild properties
/// the build must use, and the archive of the commit's source the hub publishes.
/// </summary>
internal sealed class BuildPreparer(GitCheckout git)
{
    private const string RunesmithSdkPackage = "Runesmith.Sdk";
    private static readonly string[] RequiredFiles = ["LICENSE", "README.md"];

    /// <exception cref="ToolException">The source can't be built by the hub; the message lists every problem.</exception>
    public async Task<BuildPlan> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var repositoryRoot = Path.GetFullPath(request.RepositoryRoot);
        if (request.Commit is { } commit)
            await CheckRepositoryAsync(repositoryRoot, commit, cancellationToken).ConfigureAwait(false);

        var source = PluginSource.Load(Path.Combine(repositoryRoot, request.PluginPath), request.Expectations);
        var problems = new List<string>();
        CheckFiles(source, problems);
        var runesmithSdk = source.RunesmithApi.Minimum;
        CheckRunesmithSdk(source, runesmithSdk, problems);
        if (problems.Count > 0)
            throw new ToolException("The plugin can't be built:\n" + string.Join("\n", problems.Select(problem => $"  {problem}")));

        var dotnetSdk = DotnetSdks.For(runesmithSdk);
        var output = Directory.CreateDirectory(request.Output).FullName;
        var nugetConfig = Path.Combine(output, NuGetConfigWriter.FileName);
        var properties = Path.Combine(output, BuildProperties.FileName);
        await File.WriteAllTextAsync(nugetConfig, NuGetConfigWriter.Create(request.HubFeed), cancellationToken).ConfigureAwait(false);
        var arguments = BuildProperties.Arguments(repositoryRoot, nugetConfig, source.Version, runesmithSdk);
        await File.WriteAllTextAsync(properties, BuildProperties.ResponseFile(arguments), cancellationToken).ConfigureAwait(false);
        string? archive = null;
        if (request.Commit is { } archived)
        {
            var version = source.Version.ToString();
            archive = Path.Combine(output, BuildArtifacts.Source(source.Id, version));
            var bytes = await new SourceArchiver(git).CreateAsync(repositoryRoot, archived, $"{source.Id}-{version}", cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(archive, bytes, cancellationToken).ConfigureAwait(false);
        }

        return new BuildPlan(
            source.Id,
            source.Version.ToString(),
            request.Commit ?? "local",
            source.Root,
            new BuildProjects(source.ContractsProject, source.ImplementationProject),
            dotnetSdk,
            runesmithSdk.ToString(),
            nugetConfig,
            properties,
            arguments,
            archive);
    }

    private async Task CheckRepositoryAsync(string repositoryRoot, string commit, CancellationToken cancellationToken)
    {
        var head = await git.HeadCommitAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(head, commit, StringComparison.OrdinalIgnoreCase))
            throw new ToolException($"The checkout is at {head}, not the release commit {commit}.");
        var submodules = await git.SubmodulesAsync(repositoryRoot, commit, cancellationToken).ConfigureAwait(false);
        if (submodules.Count > 0 || File.Exists(Path.Combine(repositoryRoot, ".gitmodules")))
            throw new ToolException($"Submodules are not supported; the code a plugin needs must be in its repository. Found: {string.Join(", ", submodules.DefaultIfEmpty(".gitmodules"))}.");
    }

    private static void CheckFiles(PluginSource source, List<string> problems)
    {
        var manifest = source.Manifest;
        IEnumerable<string> required =
        [
            .. RequiredFiles, manifest.Icon!, .. manifest.Banner is { } banner ? [banner] : Array.Empty<string>(),
            .. (manifest.Gallery ?? []).Select(image => image.Path!), manifest.Projects!.Contracts!, manifest.Projects.Implementation!,
        ];
        problems.AddRange(required.Where(file => !File.Exists(source.Resolve(file))).Select(file => $"{file} is missing."));

        foreach (var project in (string[])[source.ContractsProject, source.ImplementationProject])
        {
            if (File.Exists(project) && !File.Exists(LockFile.PathFor(project)))
                problems.Add($"{Path.GetRelativePath(source.Root, LockFile.PathFor(project)).Replace('\\', '/')} is missing; set RestorePackagesWithLockFile to true and commit the lock file.");
        }
    }

    private static void CheckRunesmithSdk(PluginSource source, SemanticVersion minimum, List<string> problems)
    {
        foreach (var project in (string[])[source.ContractsProject, source.ImplementationProject])
        {
            var lockFile = LockFile.PathFor(project);
            if (!File.Exists(lockFile))
                continue;
            foreach (var package in LockFile.Read(source.Root, lockFile).Where(package => string.Equals(package.Id, RunesmithSdkPackage, StringComparison.OrdinalIgnoreCase)))
            {
                if (!SemanticVersion.TryParse(package.Version, out var version) || version != minimum)
                    problems.Add($"{Path.GetFileName(project)} locks {RunesmithSdkPackage} {package.Version}; it must reference {minimum}, the minimum of runesmithApi.");
            }
        }
    }
}
