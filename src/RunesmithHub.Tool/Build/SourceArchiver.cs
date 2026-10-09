using System.IO.Compression;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Build;

/// <summary>Archives the exact tree of a commit as a gzip-compressed tar file; the same commit always gives the same bytes.</summary>
internal sealed class SourceArchiver(GitCheckout git)
{
    public async Task<byte[]> CreateAsync(string directory, string commit, string prefix, CancellationToken cancellationToken = default)
    {
        var root = await git.TopLevelAsync(directory, cancellationToken).ConfigureAwait(false);
        var tar = Path.Combine(Path.GetTempPath(), $"rshub-source-{Guid.NewGuid():N}.tar");
        try
        {
            await git.ArchiveAsync(root, commit, prefix, tar, cancellationToken).ConfigureAwait(false);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            await using (var input = File.OpenRead(tar))
                await input.CopyToAsync(gzip, cancellationToken).ConfigureAwait(false);
            return output.ToArray();
        }
        finally
        {
            File.Delete(tar);
        }
    }
}
