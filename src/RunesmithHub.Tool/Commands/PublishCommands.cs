using System.CommandLine;
using System.Globalization;
using RunesmithHub.Tool.IndexBuild;
using RunesmithHub.Tool.Publishing;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Commands;

/// <summary><c>rshub publish</c>: releases approved versions and announces them.</summary>
internal static class PublishCommands
{
    public static Command Create(ToolContext context)
    {
        var publish = new Command("publish", "Release versions to the packages repository and the hub feed.");
        publish.Add(Artifacts(context));
        publish.Add(Announce(context));
        publish.Add(Withdraw(context));
        return publish;
    }

    private static Command Artifacts(ToolContext context)
    {
        var registry = CommonOptions.Registry();
        var index = CommonOptions.Index();
        var work = new Option<string>("--work") { Description = "A folder for the downloaded build outputs.", Required = true };
        var output = CommonOptions.Output("The publications file to write for index publish and publish announce.");
        var command = new Command("artifacts",
            "Release every merged version the index has not published: check its build output against the record, create its release and push its contracts package.")
        {
            registry, index, work, output,
        };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var reader = new RegistryReader(new DirectoryRegistryFiles(result.GetRequiredValue(registry)));
            var trust = TrustRoot.Verify(await reader.RootsAsync(cancellationToken).ConfigureAwait(false));
            var current = IndexState.Read(result.GetRequiredValue(index), trust);
            var publisher = new ArtifactPublisher(context.Services.GitHub(), context.Services.Bot(), context.Services.Artifacts(), context.Services.Feed(), context.Time,
                CommonOptions.BotLogin(context));
            var (publications, notes) = await publisher.PublishAsync(await reader.LoadAsync(cancellationToken).ConfigureAwait(false), current ?? IndexState.Empty,
                result.GetRequiredValue(work), cancellationToken).ConfigureAwait(false);
            Publication.Write(result.GetRequiredValue(output), publications);
            ActionsOutput.Set(context, "count", publications.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var note in notes)
                await writer.WriteLineAsync(note).ConfigureAwait(false);
            return 0;
        }));
        return command;
    }

    private static Command Withdraw(ToolContext context)
    {
        var registry = CommonOptions.Registry();
        var command = new Command("withdraw",
            "Delete the release files of blocked versions, keeping the source archive unless the block is for legal reasons, when legal also the contracts package.")
        {
            registry,
        };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var state = await new RegistryReader(new DirectoryRegistryFiles(result.GetRequiredValue(registry))).LoadAsync(cancellationToken).ConfigureAwait(false);
            var remover = new BlockedVersionRemover(context.Services.GitHub(), context.Services.Bot(), context.Services.Feed());
            foreach (var note in await remover.RemoveAsync(state, cancellationToken).ConfigureAwait(false))
                await writer.WriteLineAsync(note).ConfigureAwait(false);
            return 0;
        }));
        return command;
    }

    private static Command Announce(ToolContext context)
    {
        var publications = new Option<string>("--publications") { Description = "The publications file from publish artifacts.", Required = true };
        var command = new Command("announce", "Comment where each published version is on its tracking issue and close it.") { publications };
        command.SetAction((result, cancellationToken) => CommandRunner.RunAsync(result, async writer =>
        {
            var list = Publication.Read(result.GetRequiredValue(publications));
            await new PublishAnnouncer(context.Services.Bot()).AnnounceAsync(list, cancellationToken).ConfigureAwait(false);
            await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Announced {list.Count} version(s).")).ConfigureAwait(false);
            return 0;
        }));
        return command;
    }
}
