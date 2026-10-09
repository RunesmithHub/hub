namespace RunesmithHub.Protocol;

/// <summary>The plugin categories of the default policy.</summary>
public static class Categories
{
    /// <summary>Gets the default category list, in the order it is shown.</summary>
    public static IReadOnlyList<string> Default { get; } =
    [
        "Languages", "Version control", "Themes", "Formatters and linters", "Debugging", "Testing", "Build and run", "Snippets and templates",
        "Navigation", "Productivity", "Visualization", "Cloud and remote", "Data and databases", "Documentation", "Education", "Other",
    ];
}
