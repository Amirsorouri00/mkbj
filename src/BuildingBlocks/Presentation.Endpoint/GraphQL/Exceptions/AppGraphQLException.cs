using EwaysVNext.Outcomes;

namespace EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;

public abstract class AppGraphQLException(Error error) : Exception(error.Message)
{
    public string Code { get; } = error.Code;
    public int HttpStatus { get; } = error.HttpStatus;
    public bool Retryable { get; } = error.Retryable;
}
