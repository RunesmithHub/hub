namespace RunesmithHub.Tool;

/// <summary>A problem the user can fix, reported as a message without a stack trace.</summary>
internal sealed class ToolException : Exception
{
    public ToolException()
    {
    }

    public ToolException(string message)
        : base(message)
    {
    }

    public ToolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
