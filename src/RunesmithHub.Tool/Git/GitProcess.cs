using System.Globalization;
using RunesmithHub.Tool.Processes;

namespace RunesmithHub.Tool.Git;

/// <summary>
/// Runs <c>git</c> against one repository's own Git folder and work tree, named explicitly, with only that repository's configuration:
/// git never searches for a repository, and system and user configuration, hooks, file system monitors and inherited <c>GIT_*</c>
/// variables are left out.
/// </summary>
internal static class GitProcess
{
    public const string GitFolder = ".git";

    private static readonly (string Key, string Value)[] Settings =
    [
        ("core.hooksPath", "/dev/null"),
        ("core.fsmonitor", "false"),
        ("core.attributesFile", "/dev/null"),
        ("protocol.allow", "never"),
    ];

    /// <summary>Gets the environment variables for git commands on the repository at a root folder.</summary>
    public static IReadOnlyDictionary<string, string?> Environment(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in System.Environment.GetEnvironmentVariables().Keys.OfType<string>())
        {
            if (name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase))
                environment[name] = null;
        }

        environment["GIT_DIR"] = Path.Combine(root, GitFolder);
        environment["GIT_WORK_TREE"] = root;
        environment["GIT_CEILING_DIRECTORIES"] = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? root;
        environment["GIT_CONFIG_NOSYSTEM"] = "1";
        environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        environment["GIT_ATTR_NOSYSTEM"] = "1";
        environment["GIT_TERMINAL_PROMPT"] = "0";
        environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        environment["GIT_OPTIONAL_LOCKS"] = "0";
        environment["GIT_CONFIG_COUNT"] = Settings.Length.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < Settings.Length; i++)
        {
            environment[string.Create(CultureInfo.InvariantCulture, $"GIT_CONFIG_KEY_{i}")] = Settings[i].Key;
            environment[string.Create(CultureInfo.InvariantCulture, $"GIT_CONFIG_VALUE_{i}")] = Settings[i].Value;
        }

        return environment;
    }

    /// <summary>Runs a git command on the repository at a root folder, from that folder.</summary>
    public static Task<ProcessResult> RunAsync(IProcessRunner processes, string repositoryRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processes);
        return processes.RunAsync("git", arguments, Path.GetFullPath(repositoryRoot), Environment(repositoryRoot), cancellationToken);
    }

    /// <summary>Finds the nearest folder at or above a folder that holds a <c>.git</c> folder or file, or returns null.</summary>
    public static string? FindRepositoryRoot(string directory)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(directory)); current is not null; current = current.Parent)
        {
            var git = Path.Combine(current.FullName, GitFolder);
            if (Directory.Exists(git) || File.Exists(git))
                return current.FullName;
        }

        return null;
    }
}
