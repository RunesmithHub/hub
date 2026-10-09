using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.GitHub;

namespace RunesmithHub.Tool.Audit;

/// <summary>One checkable verification requirement and whether it is met; null when the token could not tell.</summary>
internal sealed record RequirementCheck(string Requirement, bool? Met);

/// <summary>Checks the verification requirements GitHub's public API can answer; an administrator checks the rest.</summary>
internal sealed class VerifiedRequirements(IRepositoryReader github, TimeProvider time)
{
    private static readonly TimeSpan MinimumAccountAge = TimeSpan.FromDays(90);
    private static readonly string[] SecurityPolicyPaths = ["SECURITY.md", ".github/SECURITY.md", "docs/SECURITY.md"];

    public async Task<IReadOnlyList<RequirementCheck>> CheckAsync(PublisherFile publisher, GitHubRepository repository, CancellationToken cancellationToken)
    {
        var owner = await github.GetUserAsync(publisher.Owner.Id, cancellationToken).ConfigureAwait(false);
        var rules = await github.GetBranchRulesAsync(repository.FullName, repository.DefaultBranch, cancellationToken).ConfigureAwait(false);
        var securityPolicy = false;
        foreach (var path in SecurityPolicyPaths)
            securityPolicy |= await github.GetFileAsync(repository.FullName, path, null, cancellationToken).ConfigureAwait(false) is not null;
        var reporting = securityPolicy ? true : await github.IsPrivateReportingEnabledAsync(repository.FullName, cancellationToken).ConfigureAwait(false);

        return
        [
            new("The publisher's GitHub account is at least 90 days old", owner is not null && time.GetUtcNow() - owner.CreatedAt >= MinimumAccountAge),
            new("The publisher's account has not been renamed", owner is not null && string.Equals(owner.Login, publisher.Owner.Login, StringComparison.OrdinalIgnoreCase)),
            new("The default branch blocks force pushes", rules.BlocksForcePushes),
            new("The default branch blocks deletion", rules.BlocksDeletion),
            new("The repository has a security contact (SECURITY.md or private vulnerability reporting)", securityPolicy || reporting == true ? true : reporting),
        ];
    }

    /// <summary>Formats the checks as a task list.</summary>
    public static string Checklist(IEnumerable<RequirementCheck> checks) =>
        string.Join('\n', checks.Select(check => check.Met switch
        {
            true => $"- [x] {check.Requirement}",
            false => $"- [ ] {check.Requirement} (not met)",
            null => $"- [ ] {check.Requirement} (an administrator checks this)",
        }));
}
