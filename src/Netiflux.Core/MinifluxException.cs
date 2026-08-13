using System.Net;

namespace Netiflux.Core;

/// <summary>
/// Raised for any non-success response from the Miniflux API, and for transport failures.
/// Carries the status code so callers can distinguish "your token is wrong" (401) from
/// "the server is down", which are very different messages to put in front of a user.
/// </summary>
public sealed class MinifluxException : Exception
{
    public MinifluxException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }

    public bool IsAuthFailure =>
        StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    /// <summary>A short, non-technical explanation suitable for the status bar.</summary>
    public string UserMessage => StatusCode switch
    {
        HttpStatusCode.Unauthorized => "Authentication failed — check your API token.",
        HttpStatusCode.Forbidden => "Access denied by the server.",
        HttpStatusCode.NotFound => "Not found on the server.",
        HttpStatusCode.InternalServerError => "Miniflux returned a server error.",
        null => Message,
        _ => Message
    };
}
