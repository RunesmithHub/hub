using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.IndexBuild;

/// <summary>Which index build to run.</summary>
internal enum IndexOperation
{
    Publish,
    Moderate,
    Timestamp,
    Refresh,
}

/// <summary>Where an index build reads from and writes to.</summary>
/// <param name="Index">The index directory, such as a checkout of the index branch; the build reads it and writes the new index into it.</param>
/// <param name="Publications">The publications file from <c>publish artifacts</c>, for <see cref="IndexOperation.Publish"/>.</param>
internal sealed record IndexJobOptions(string Registry, string Index, string? Publications);

/// <summary>What an index build did, and whether the index still has no moderation list.</summary>
internal sealed record IndexJobResult(IReadOnlyList<string> Notes, bool Bootstrap);

/// <summary>Loads the trust root and the index directory, runs one index build, and writes the result back into the directory.</summary>
internal sealed class IndexJob(ToolContext context)
{
    public async Task<IndexJobResult> RunAsync(IndexOperation operation, IndexJobOptions options, CancellationToken cancellationToken)
    {
        var reader = new RegistryReader(new DirectoryRegistryFiles(options.Registry));
        var trust = TrustRoot.Verify(await reader.RootsAsync(cancellationToken).ConfigureAwait(false));
        var current = IndexState.Read(options.Index, trust);
        var basis = current ?? IndexState.Empty;
        var notes = new List<string>();
        if (current is null)
            notes.Add("The index is empty: bootstrapping it.");

        using var keys = OnlineKeys.FromEnvironment(context.GetEnvironmentVariable);
        var builder = new IndexBuilder(context.Time);
        var result = operation switch
        {
            IndexOperation.Publish => builder.Publish(basis, await reader.LoadAsync(cancellationToken).ConfigureAwait(false),
                options.Publications is null ? [] : Publication.Read(options.Publications), keys),
            IndexOperation.Moderate => builder.Moderate(basis, await reader.LoadAsync(cancellationToken).ConfigureAwait(false), keys),
            IndexOperation.Timestamp => builder.Timestamp(basis, keys),
            _ => builder.Timestamp(basis.Timestamp is null ? throw new ToolException("The index has no timestamp yet, so there is none to refresh.") : basis, keys),
        };

        IndexOutput.Write(options.Index, trust, result.State);
        notes.AddRange(result.Notes);
        if (result.State.Timestamp is null || ReferenceEquals(result.State.Timestamp, basis.Timestamp))
            notes.Add("No new timestamp was signed: run index timestamp with HUB_TIMESTAMP_KEY to make the changes current.");
        return new IndexJobResult(notes, result.State.Moderation is null);
    }
}
