namespace RunesmithHub.Protocol.Packaging;

/// <summary>A package breaks the packaging rules.</summary>
public sealed class PackageException : Exception
{
    public PackageException()
        : this([])
    {
    }

    public PackageException(string message)
        : this([message])
    {
    }

    public PackageException(string message, Exception innerException)
        : base(message, innerException) => Problems = [message];

    public PackageException(IReadOnlyList<string> problems)
        : base(problems.Count == 0 ? "The package is not valid." : string.Join(" ", problems)) => Problems = problems;

    /// <summary>Gets every rule the package breaks.</summary>
    public IReadOnlyList<string> Problems { get; }
}
