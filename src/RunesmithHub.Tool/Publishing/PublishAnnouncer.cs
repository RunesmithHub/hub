using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.IndexBuild;

namespace RunesmithHub.Tool.Publishing;

/// <summary>Tells each published version's tracking issue where to find it, and closes the issue.</summary>
internal sealed class PublishAnnouncer(IGitHubWriter bot)
{
    public async Task AnnounceAsync(IReadOnlyList<Publication> publications, CancellationToken cancellationToken)
    {
        foreach (var publication in publications)
        {
            var text = $"Published. It is on [the hub]({HubLocations.PluginPage(publication.Plugin)}) and in [its release]({publication.Release}); " +
                "Runesmith picks it up at its next check.\n";
            await bot.CommentAsync(HubLocations.Registry, publication.TrackingIssue, text, cancellationToken).ConfigureAwait(false);
            await bot.CloseIssueAsync(HubLocations.Registry, publication.TrackingIssue, cancellationToken).ConfigureAwait(false);
        }
    }
}
