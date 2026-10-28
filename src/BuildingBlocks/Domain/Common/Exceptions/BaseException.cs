using System.Net;

namespace EwaysVNext.Domain.Common.Exceptions;

public abstract class BaseException(string message, HttpStatusCode statusCode) : Exception(message)
{
    public HttpStatusCode StatusCode { get; protected set; } = statusCode;
}
