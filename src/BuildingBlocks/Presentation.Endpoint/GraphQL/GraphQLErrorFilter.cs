using EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;
using HotChocolate;
using HotChocolate.Execution;
using Error = EwaysVNext.Outcomes.Error;

namespace EwaysVNext.Presentation.Endpoint.GraphQL;

public class GraphQLErrorFilter : IErrorFilter
{
    public IError OnError(IError error)
    {
        if (error.Exception is AppGraphQLException appEx)
            return error
                .WithMessage(appEx.Message)
                .WithCode(appEx.Code)
                .SetExtension("httpStatus", appEx.HttpStatus);

        // HotChocolate's built-in [Authorize] middleware reports these errors directly
        // (no exception attached), so they must be preserved instead of masked below.
        if (error.Code is "AUTH_NOT_AUTHORIZED" or "AUTH_NOT_AUTHENTICATED")
            return error;

        if (error.Exception is null)
            return error;

        return error
            .WithMessage(GraphQLErrorFilterMessages.UnexpectedError)
            .WithCode(Error.Internal.Code);
    }
}
