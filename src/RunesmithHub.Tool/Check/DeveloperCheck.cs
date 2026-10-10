using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Analysis;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Check;

/// <summary>
/// Runs the hub's pipeline on a developer's machine with the same rules: prepare, restore and build, package, and analyse without the
/// private heuristics. A plugin that passes here passes the hub's checks, apart from those that need the registry.
/// </summary>
internal sealed class DeveloperCheck(IProcessRunner processes, PackageAssembler assembler, PluginAnalyzer analyzer, TimeProvider time, TextWriter log)
{
    private const string BuildFolder = "build";
    private const string PackageFolder = "package";
    private const string AnalysisFolder = "analysis";

    public async Task<AnalysisResult> RunAsync(CheckRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = Path.GetFullPath(request.PluginRoot);
        var output = Path.GetFullPath(request.Output);
        foreach (var folder in (string[])[BuildFolder, PackageFolder, AnalysisFolder])
            Clear(Path.Combine(output, folder));

        var git = new GitCheckout(processes);
        var repository = GitProcess.FindRepositoryRoot(root);
        var repositoryRoot = repository ?? root;
        var commit = repository is null ? null : await CommitAsync(git, repository, cancellationToken).ConfigureAwait(false);
        var pluginPath = Path.GetRelativePath(repositoryRoot, root).Replace('\\', '/');

        await log.WriteLineAsync("Checking the source...").ConfigureAwait(false);
        var plan = await new BuildPreparer(git).PrepareAsync(
            new PrepareRequest(repositoryRoot, pluginPath, new PluginExpectations(null, null, null), commit, Path.Combine(output, BuildFolder), request.HubFeed),
            cancellationToken).ConfigureAwait(false);

        if (request.Build)
        {
            await log.WriteLineAsync("Restoring and building in Release...").ConfigureAwait(false);
            await DotnetAsync(root, ["restore", plan.Projects.Implementation, .. plan.PropertyArguments], cancellationToken).ConfigureAwait(false);
            await DotnetAsync(root, ["build", plan.Projects.Implementation, "--no-restore", .. plan.PropertyArguments], cancellationToken).ConfigureAwait(false);
        }

        await log.WriteLineAsync("Packaging...").ConfigureAwait(false);
        var sdk = (await processes.RunAsync("dotnet", ["--version"], root, cancellationToken).ConfigureAwait(false)).OutputOrThrow("dotnet --version").Trim();
        var package = await assembler.AssembleAsync(
            new PackageRequest(root, Path.Combine(output, PackageFolder), commit ?? new string('0', 40), "local", sdk, plan.SourceArchive),
            cancellationToken).ConfigureAwait(false);

        await log.WriteLineAsync("Analysing...").ConfigureAwait(false);
        var registration = Registration(package.Manifest.Id, package.Manifest.Repository, pluginPath);
        var source = new AnalysisSource(repositoryRoot, package.Manifest.Build.Commit);
        var result = await analyzer.AnalyseAsync(new AnalysisRequest(package.PackageFile, null, registration, source, null), cancellationToken).ConfigureAwait(false);
        await Analysis.Reporting.AnalysisOutput.WriteAsync(result, Path.Combine(output, AnalysisFolder), null, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async Task<string?> CommitAsync(GitCheckout git, string repositoryRoot, CancellationToken cancellationToken)
    {
        try
        {
            return await git.HeadCommitAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (ToolException)
        {
            return null;
        }
    }

    private async Task DotnetAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("dotnet", arguments, directory, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new ToolException($"dotnet {arguments[0]} failed:\n{result.Output}{result.Error}".TrimEnd());
    }

    private Registration Registration(string id, string repository, string pluginPath) => new()
    {
        Id = id,
        Repository = new RegisteredRepository { Id = 0, Url = repository, Path = pluginPath },
        Maintainers = [],
        Tier = PluginTier.Unverified,
        State = RegistrationState.Registered,
        RegisteredAt = time.GetUtcNow(),
    };

    private static void Clear(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
