using System.CommandLine;
using RunesmithHub.Protocol.Packaging;
using RunesmithHub.Tool.Build;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.Json;
using RunesmithHub.Tool.NuGet;
using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub build</c>: the steps of the untrusted build job, and the comparison of two builds.</summary>
internal static class BuildCommands
{
    public static Command Create(ToolContext context)
    {
        var build = new Command("build", "Prepare, package and compare hub builds.");
        build.Add(Prepare(context));
        build.Add(Package(context));
        build.Add(Compare());
        return build;
    }

    /// <summary>Creates the option for the hub feed's service index URL.</summary>
    public static Option<string> FeedOption() =>
        new("--feed") { Description = $"The hub feed's service index URL; {NuGetFeeds.HubFeedVariable} or {NuGetFeeds.DefaultHubFeed} by default." };

    /// <summary>Creates the source of bundled packages: a local folder first when one is given, then the feeds.</summary>
    public static INuGetPackageSource PackageSource(ToolContext context, string? feed, string? cache)
    {
        INuGetPackageSource http = new HttpNuGetPackageSource(context.Services.Http(), NuGetFeeds.HubFeed(feed, context.GetEnvironmentVariable));
        return cache is null ? http : new FolderNuGetPackageSource(cache, http);
    }

    /// <summary>Gets the version of the .NET SDK that <c>dotnet</c> runs in a folder.</summary>
    public static async Task<string> DotnetSdkAsync(ToolContext context, string directory, CancellationToken cancellationToken) =>
        (await context.Services.Processes.RunAsync("dotnet", ["--version"], directory, cancellationToken).ConfigureAwait(false)).OutputOrThrow("dotnet --version").Trim();

    private static Command Prepare(ToolContext context)
    {
        var source = new Option<string>("--source") { Description = "The checkout of the plugin's repository at the release commit.", Required = true };
        var registration = new Option<string>("--registration") { Description = "The plugin's registration.json.", Required = true };
        var tag = new Option<string>("--tag") { Description = "The release tag, such as v1.4.0.", Required = true };
        var commit = new Option<string>("--commit") { Description = "The full SHA of the commit the tag points to.", Required = true };
        var output = new Option<string>("--output") { Description = "The folder to write the NuGet configuration and MSBuild properties to.", DefaultValueFactory = _ => "hub-build" };
        var feed = FeedOption();
        var command = new Command("prepare", "Check the plugin's source at the release commit and print the build plan as JSON.") { source, registration, tag, commit, output, feed };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var plugin = RegistrationFile.Read(result.GetRequiredValue(registration));
            var request = new PrepareRequest(
                result.GetRequiredValue(source),
                plugin.Repository.Path,
                PluginExpectations.ForRelease(plugin, result.GetRequiredValue(tag)),
                result.GetRequiredValue(commit),
                result.GetRequiredValue(output),
                NuGetFeeds.HubFeed(result.GetValue(feed), context.GetEnvironmentVariable));
            var plan = await new BuildPreparer(new GitCheckout(context.Services.Processes)).PrepareAsync(request, cancellationToken).ConfigureAwait(false);
            ActionsOutput.Set(context, "plugin-root", plan.PluginRoot);
            ActionsOutput.Set(context, "implementation", plan.Projects.Implementation);
            ActionsOutput.Set(context, "properties", plan.Properties);
            ActionsOutput.Set(context, "dotnet-sdk", plan.DotnetSdk);
            await writer.WriteAsync(ArtifactJson.SerializeToString(plan)).ConfigureAwait(false);
            return 0;
        }));
        return command;
    }

    private static Command Package(ToolContext context)
    {
        var source = new Option<string>("--source") { Description = "The plugin's folder, restored and built.", Required = true };
        var output = new Option<string>("--output") { Description = "An empty folder for the package and everything published beside it.", Required = true };
        var commit = new Option<string>("--commit") { Description = "The full SHA of the commit that was built.", Required = true };
        var run = new Option<string>("--run") { Description = "The URL of the pipeline run.", Required = true };
        var registration = new Option<string>("--registration") { Description = "The plugin's registration.json, for raised size limits." };
        var cache = new Option<string>("--package-cache") { Description = "A folder of .nupkg files to read packages from before downloading them." };
        var sdk = new Option<string>("--sdk") { Description = "The version of the .NET SDK that built the plugin; what dotnet --version prints in the plugin's folder by default." };
        var feed = FeedOption();
        var command = new Command("package", "Assemble the package, images, SBOM, source archive and hash list from a built plugin.")
        {
            source, output, commit, run, registration, cache, sdk, feed,
        };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var root = result.GetRequiredValue(source);
            var limits = result.GetValue(registration) is { } path ? PackageLimits.From(RegistrationFile.Read(path).Limits) : PackageLimits.Default;
            var request = new PackageRequest(
                root, result.GetRequiredValue(output), result.GetRequiredValue(commit), result.GetRequiredValue(run),
                result.GetValue(sdk) ?? await DotnetSdkAsync(context, root, cancellationToken).ConfigureAwait(false), ArchiveSource: true)
            { Limits = limits };
            var git = new GitCheckout(context.Services.Processes);
            var assembler = new PackageAssembler(
                new BundleVerifier(PackageSource(context, result.GetValue(feed), result.GetValue(cache))), new SourceArchiver(git), SharedFrameworks.DefaultRoots(context.GetEnvironmentVariable));
            var package = await assembler.AssembleAsync(request, cancellationToken).ConfigureAwait(false);
            await writer.WriteLineAsync($"Wrote {package.PackageFile}; {HashList.FileName} lists all {package.Hashes.Files.Count} output files.").ConfigureAwait(false);
            return 0;
        }));
        return command;
    }

    private static Command Compare()
    {
        var first = new Argument<string>("first") { Description = "The hashes.json of one build." };
        var second = new Argument<string>("second") { Description = "The hashes.json of the other build." };
        var command = new Command("compare", "Compare the hash lists of two builds; exit code 1 lists the files that differ.") { first, second };
        command.SetAction(result => CommandRunner.Run(result, writer =>
        {
            var differences = HashList.Read(result.GetRequiredValue(first)).Differences(HashList.Read(result.GetRequiredValue(second)));
            if (differences.Count == 0)
            {
                writer.WriteLine("The builds are identical.");
                return 0;
            }

            writer.WriteLine("The builds differ in:");
            foreach (var path in differences)
                writer.WriteLine($"  {path}");
            return 1;
        }));
        return command;
    }
}
