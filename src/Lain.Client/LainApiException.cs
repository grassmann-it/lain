using System.Net;

namespace Lain.Client;

public sealed class LainApiException(HttpStatusCode statusCode, string code, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
