namespace RunesmithHub.Protocol.Updating;

/// <summary>The index host could not be reached or did not answer properly.</summary>
public sealed class IndexUnavailableException : Exception
{
    public IndexUnavailableException()
    {
    }

    public IndexUnavailableException(string message)
        : base(message)
    {
    }

    public IndexUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
