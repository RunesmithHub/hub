using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Registry;
using RunesmithHub.Tool.Git;
using RunesmithHub.Tool.GitHub;
using RunesmithHub.Tool.Registry;

namespace RunesmithHub.Tool.Registrations;

/// <summary>The registration check of publisher and registration files; what the token cannot read becomes a warning for the reviewer.</summary>
internal sealed class RegistrationValidator(IGitHubReader github)
{
    private const int MaxDisplayName = 60;

    /// <param name="author">The pull request's author, by permanent ID and login.</param>
    public async Task<ValidationReport> ValidateAsync(
        IRegistryFiles head, IRegistryFiles baseFiles, IReadOnlyList<ChangedPath> changes, GitHubAccount author, CancellationToken cancellationToken)
    {
        var report = new ValidationReport();
        var before = new RegistryReader(baseFiles);
        var after = new RegistryReader(head);
        var reserved = await after.ReservedNamesAsync(cancellationToken).ConfigureAwait(false);
        var existingPublishers = await before.PublishersAsync(cancellationToken).ConfigureAwait(false);
        var existingPlugins = await before.RegistrationsAsync(cancellationToken).ConfigureAwait(false);

        foreach (var change in changes.OrderBy(change => change.Path, StringComparer.Ordinal))
        {
            var isPublisher = RegistryPaths.IsPublisher(change.Path, out var publisherName);
            var isRegistration = RegistryPaths.IsRegistration(change.Path, out var pluginId);
            if (!isPublisher && !isRegistration)
                continue;
            if (change.Change == FileChange.Deleted)
            {
                report.Fail(change.Path, "Format", "Publisher and registration records are never deleted; change their state instead.");
                continue;
            }

            var bytes = (await head.ReadAsync(change.Path, cancellationToken).ConfigureAwait(false))!;
            var isNew = change.Change == FileChange.Added;
            try
            {
                if (isPublisher)
                {
                    var publisher = RegistryReader.Parse<PublisherFile>(bytes, change.Path);
                    await CheckPublisherAsync(report, change.Path, publisherName!, publisher, isNew, reserved, existingPublishers, author, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var registration = RegistryReader.Parse<RunesmithHub.Protocol.Registry.Registration>(bytes, change.Path);
                    var publisher = await after.PublisherAsync(PluginId.TryParse(registration.Id, out var id) ? id.Publisher : "", cancellationToken).ConfigureAwait(false);
                    var all = await after.RegistrationsAsync(cancellationToken).ConfigureAwait(false);
                    await CheckRegistrationAsync(report, change.Path, pluginId!, registration, publisher, isNew, reserved, existingPlugins, all, author, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (ToolException exception)
            {
                report.Fail(change.Path, "Format", exception.Message);
            }
        }

        return report;
    }

    private async Task CheckPublisherAsync(
        ValidationReport report, string path, string fileName, PublisherFile publisher, bool isNew, IReadOnlySet<string> reserved,
        IReadOnlyList<PublisherFile> existing, GitHubAccount author, CancellationToken cancellationToken)
    {
        var format = new List<string>();
        if (!PublisherName.IsValid(publisher.Name))
            format.Add("the name must be 2 to 40 lowercase letters, digits and single hyphens, starting with a letter");
        if (publisher.Name != fileName)
            format.Add($"the name must match the file name {fileName}");
        if (publisher.DisplayName.Trim().Length == 0 || publisher.DisplayName.Length > MaxDisplayName || publisher.DisplayName.Contains('\n', StringComparison.Ordinal))
            format.Add($"the display name must be one line of 1 to {MaxDisplayName} characters");
        if (isNew && (publisher.State != PublisherState.Active || publisher.Verified is not null || publisher.Limits is not null))
            format.Add("a new publisher is active, unverified and has no raised limits");
        Report(report, path, "Format", format, "The record is well formed.");

        if (isNew)
        {
            Report(report, path, "Reserved names", reserved.Contains(publisher.Name) ? [$"\"{publisher.Name}\" is reserved"] : [], "The name is not reserved.");
            Similarity(report, path, [publisher.Name, publisher.DisplayName], existing.SelectMany(other => new[] { other.Name, other.DisplayName }));
        }

        var owner = await github.GetUserAsync(publisher.Owner.Id, cancellationToken).ConfigureAwait(false);
        if (owner is null)
            report.Fail(path, "Ownership", $"No GitHub account has the ID {publisher.Owner.Id}.");
        else if (!string.Equals(owner.Login, publisher.Owner.Login, StringComparison.OrdinalIgnoreCase) || owner.Type != publisher.Owner.Type)
            report.Fail(path, "Ownership", $"GitHub account {publisher.Owner.Id} is {owner.Type} {owner.Login}, not {publisher.Owner.Type} {publisher.Owner.Login}.");
        else
            report.Pass(path, "Ownership", $"The owner is {owner.Type} {owner.Login} ({owner.Id}).");

        if (isNew && owner is not null)
            await CheckOwnerAuthorityAsync(report, path, owner, author, cancellationToken).ConfigureAwait(false);
    }

    private async Task CheckRegistrationAsync(
        ValidationReport report, string path, string folder, RunesmithHub.Protocol.Registry.Registration registration, PublisherFile? publisher, bool isNew, IReadOnlySet<string> reserved,
        IReadOnlyList<RunesmithHub.Protocol.Registry.Registration> existing, IReadOnlyList<RunesmithHub.Protocol.Registry.Registration> all, GitHubAccount author, CancellationToken cancellationToken)
    {
        var format = new List<string>();
        var validId = PluginId.TryParse(registration.Id, out var id);
        if (!validId)
            format.Add("the ID must be <publisher>.<name>, each part 2 to 40 lowercase letters, digits and single hyphens");
        if (registration.Id != folder)
            format.Add($"the ID must match the folder plugins/{folder}");
        if (publisher is null)
            format.Add("the publisher has no record in publishers/");
        if (!registration.Repository.Url.StartsWith("https://github.com/", StringComparison.Ordinal) || registration.Repository.Url.Count(c => c == '/') != 4)
            format.Add("the repository URL must be https://github.com/<owner>/<repo>");
        if (registration.Repository.Path != "." && !RelativePaths.IsSafe(registration.Repository.Path))
            format.Add("the repository path must be . or a relative folder with forward slashes");
        if (registration.Repository.TagPrefix.Length == 0 || registration.Repository.TagPrefix.Any(c => char.IsWhiteSpace(c) || c is '~' or '^' or ':' or '?' or '*' or '['))
            format.Add("the tag prefix must be a non-empty tag fragment such as v");
        if (registration.Maintainers.Count == 0)
            format.Add("at least one maintainer is needed");
        if (isNew && registration.State != RegistrationState.Registered)
            format.Add("a new registration has the state registered");
        if (isNew && registration.Tier != PluginTier.Unverified && !(registration.Tier == PluginTier.Official && id.Publisher == "runesmith"))
            format.Add("a new registration is unverified (official only for the runesmith publisher)");
        if (isNew && registration.Limits is not null)
            format.Add("raised limits are set by an administrator");
        Report(report, path, "Format", format, "The record is well formed.");
        if (!validId || publisher is null)
            return;

        if (isNew)
        {
            Report(report, path, "Reserved names", reserved.Contains(id.Name) ? [$"\"{id.Name}\" is reserved"] : [], "The name is not reserved.");
            Similarity(report, path, [id.Name, registration.Id], existing.Where(other => other.Id != registration.Id).SelectMany(other => new[] { PluginId.Parse(other.Id).Name, other.Id }));
        }

        var duplicates = all.Where(other => other.Id != registration.Id && other.Repository.Id == registration.Repository.Id
            && string.Equals(other.Repository.Path.TrimEnd('/'), registration.Repository.Path.TrimEnd('/'), StringComparison.Ordinal)).Select(other => other.Id).ToList();
        Report(report, path, "One plugin per repository", duplicates.Count > 0 ? [$"the repository folder is registered already as {string.Join(", ", duplicates)}"] : [],
            "No other plugin uses this repository folder.");

        var repository = await github.GetRepositoryAsync(registration.Repository.Id, cancellationToken).ConfigureAwait(false);
        if (repository is null)
        {
            report.Fail(path, "Repository", $"No public repository has the ID {registration.Repository.Id}.");
            return;
        }

        await CheckRepositoryAsync(report, path, registration, repository, cancellationToken).ConfigureAwait(false);
        if (repository.Owner.Id != publisher.Owner.Id)
            report.Fail(path, "Ownership", $"The repository belongs to {repository.Owner.Login} ({repository.Owner.Id}), not to the publisher's owner {publisher.Owner.Login} ({publisher.Owner.Id}).");
        else
            report.Pass(path, "Ownership", "The repository belongs to the publisher's owner.");

        if (isNew)
            await CheckRepositoryAuthorityAsync(report, path, repository, publisher, author, cancellationToken).ConfigureAwait(false);
        await CheckMaintainersAsync(report, path, registration, repository, cancellationToken).ConfigureAwait(false);
    }

    private async Task CheckRepositoryAsync(ValidationReport report, string path, RunesmithHub.Protocol.Registry.Registration registration, GitHubRepository repository, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        if (repository.Private)
            problems.Add("the repository is private");
        if (repository.Fork)
            problems.Add("the repository is a fork");
        if (!string.Equals(repository.HtmlUrl, registration.Repository.Url, StringComparison.OrdinalIgnoreCase))
            problems.Add($"repository {registration.Repository.Id} is {repository.HtmlUrl}, not {registration.Repository.Url}");

        var manifestPath = registration.Repository.Path is "." or "" ? DeveloperManifest.FileName : $"{registration.Repository.Path.TrimEnd('/')}/{DeveloperManifest.FileName}";
        var manifest = await github.GetFileAsync(repository.FullName, manifestPath, null, cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            problems.Add($"the default branch has no {manifestPath}");
        }
        else
        {
            var result = ManifestValidator.Validate(manifest, new ManifestValidationOptions { ExpectedId = registration.Id, ExpectedRepository = registration.Repository.Url });
            foreach (var problem in result.Problems)
            {
                if (problem.Path is "" or "id" or "repository" or "license" or "schemaVersion")
                    problems.Add($"{manifestPath}: {problem}");
                else
                    report.Warn(path, "Manifest", $"{manifestPath}: {problem} (fix it before the first release)");
            }
        }

        Report(report, path, "Repository", problems, $"{repository.FullName} is public, not a fork, and its {manifestPath} has this ID and an OSI-approved license.");
    }

    private async Task CheckOwnerAuthorityAsync(ValidationReport report, string path, GitHubUser owner, GitHubAccount author, CancellationToken cancellationToken)
    {
        if (owner.Type == OwnerType.User)
        {
            Report(report, path, "Authority", owner.Id == author.Id ? [] : [$"@{author.Login} is not the owner @{owner.Login}"], "The author owns the account.");
            return;
        }

        switch (await github.IsOrganizationOwnerAsync(owner.Login, author.Login, cancellationToken).ConfigureAwait(false))
        {
            case true:
                report.Pass(path, "Authority", $"@{author.Login} owns the organization {owner.Login}.");
                break;
            case false:
                report.Fail(path, "Authority", $"@{author.Login} is not an owner of the organization {owner.Login}.");
                break;
            default:
                report.Warn(path, "Authority", $"Could not read whether @{author.Login} owns {owner.Login}; the reviewer confirms it.");
                break;
        }
    }

    private async Task CheckRepositoryAuthorityAsync(
        ValidationReport report, string path, GitHubRepository repository, PublisherFile publisher, GitHubAccount author, CancellationToken cancellationToken)
    {
        if (publisher.Owner.Type == OwnerType.Organization && await github.IsOrganizationOwnerAsync(publisher.Owner.Login, author.Login, cancellationToken).ConfigureAwait(false) == true)
        {
            report.Pass(path, "Authority", $"@{author.Login} owns the organization {publisher.Owner.Login}.");
            return;
        }

        var permission = author.Id == repository.Owner.Id ? RepositoryPermission.Admin : await github.GetPermissionAsync(repository.FullName, author.Login, cancellationToken).ConfigureAwait(false);
        switch (permission)
        {
            case RepositoryPermission.Admin or RepositoryPermission.Maintain:
                report.Pass(path, "Authority", $"@{author.Login} has {permission.ToString().ToLowerInvariant()} permission on {repository.FullName}.");
                break;
            case RepositoryPermission.Unknown:
                report.Warn(path, "Authority", $"Could not read @{author.Login}'s permission on {repository.FullName}; the reviewer confirms admin or maintain permission.");
                break;
            default:
                report.Fail(path, "Authority", $"@{author.Login} needs admin or maintain permission on {repository.FullName}.");
                break;
        }
    }

    private async Task CheckMaintainersAsync(ValidationReport report, string path, RunesmithHub.Protocol.Registry.Registration registration, GitHubRepository repository, CancellationToken cancellationToken)
    {
        foreach (var maintainer in registration.Maintainers)
        {
            var user = await github.GetUserAsync(maintainer.Id, cancellationToken).ConfigureAwait(false);
            if (user is null)
            {
                report.Fail(path, "Maintainers", $"No GitHub account has the ID {maintainer.Id}.");
                continue;
            }

            if (!string.Equals(user.Login, maintainer.Login, StringComparison.OrdinalIgnoreCase))
                report.Warn(path, "Maintainers", $"Account {maintainer.Id} is now @{user.Login}, not @{maintainer.Login}.");
            var permission = user.Id == repository.Owner.Id ? RepositoryPermission.Admin : await github.GetPermissionAsync(repository.FullName, user.Login, cancellationToken).ConfigureAwait(false);
            switch (permission)
            {
                case RepositoryPermission.Admin or RepositoryPermission.Maintain or RepositoryPermission.Write:
                    report.Pass(path, "Maintainers", $"@{user.Login} can write to {repository.FullName}.");
                    break;
                case RepositoryPermission.Unknown:
                    report.Warn(path, "Maintainers", $"Could not read @{user.Login}'s permission; the reviewer confirms write access.");
                    break;
                default:
                    report.Fail(path, "Maintainers", $"@{user.Login} needs write permission on {repository.FullName}.");
                    break;
            }
        }
    }

    private static void Similarity(ValidationReport report, string path, IReadOnlyList<string> candidates, IEnumerable<string> existing)
    {
        var matches = (from name in existing.Distinct(StringComparer.Ordinal)
                       from candidate in candidates
                       let reason = NameSimilarity.Compare(candidate, name)
                       where reason is not null
                       select $"\"{candidate}\" {reason}").Distinct(StringComparer.Ordinal).ToList();
        if (matches.Count == 0)
            report.Pass(path, "Similarity", "The name does not resemble an existing one.");
        foreach (var match in matches)
            report.Warn(path, "Similarity", match + "; the reviewer checks it is not an imitation.");
    }

    private static string Sentence(string text)
    {
        var startsWithPath = text.Split(' ')[0].Contains('.', StringComparison.Ordinal);
        var capitalised = startsWithPath ? text : char.ToUpperInvariant(text[0]) + text[1..];
        return capitalised.EndsWith('.') ? capitalised : capitalised + ".";
    }

    private static void Report(ValidationReport report, string path, string check, List<string> problems, string passed)
    {
        if (problems.Count == 0)
            report.Pass(path, check, passed);
        foreach (var problem in problems)
            report.Fail(path, check, Sentence(problem));
    }
}
