using System.Diagnostics.CodeAnalysis;

namespace RunesmithHub.Protocol.Updating;

/// <summary>The outcome of an index update: the newest verified view, and why the update failed if it did.</summary>
public sealed record IndexUpdateResult
{
    /// <summary>Gets the new view after a successful update, otherwise the view saved earlier, which may be null or expired.</summary>
    public IndexView? View { get; init; }

    public IndexUpdateFailure? Failure { get; init; }

    [MemberNotNullWhen(false, nameof(Failure))]
    [MemberNotNullWhen(true, nameof(View))]
    public bool Succeeded => Failure is null && View is not null;
}
