using System.Net;

namespace RunesmithHub.Tool.GitHub;

/// <summary>GitHub answered a request with an error.</summary>
internal sealed class GitHubException : Exception
{
    public GitHubException()
    {
    }

    public GitHubException(string message)
        : base(message)
    {
    }

    public GitHubException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public GitHubException(HttpStatusCode status, string message)
        : base(message) => Status = status;

    public HttpStatusCode? Status { get; }
}
