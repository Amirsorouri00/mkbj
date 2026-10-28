using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;

namespace EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;

public static class ErrorGraphQLExtensions
{
    public static Exception ToGraphQLException(this Error error) => error.Code switch
    {
        "CONCURRENCY_CONFLICT" => new ConcurrencyConflictGraphQLException(error),
        "VALIDATION" => new ValidationGraphQLException(error),
        "UNAUTHORIZED" => new UnauthorizedGraphQLException(error),
        "FORBIDDEN" => new ForbiddenGraphQLException(error),

        _ => error.HttpStatus switch
        {
            404 => new NotFoundGraphQLException(error),
            409 => new ConflictGraphQLException(error),
            403 => new ForbiddenGraphQLException(error),
            401 => new UnauthorizedGraphQLException(error),
            400 => new ValidationGraphQLException(error),
            // Deliberately a plain Exception (pinned by ErrorGraphQLExtensionsTests): it is not an
            // AppGraphQLException, so GraphQLErrorFilter masks it as an unexpected internal error.
#pragma warning disable CA2201
            _ => new Exception(error.Message)
#pragma warning restore CA2201
        }
    };
}
