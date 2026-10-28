using EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;
using HotChocolate;
using HotChocolate.Execution;
using Error = EwaysVNext.Outcomes.Error;

namespace EwaysVNext.Presentation.Endpoint.GraphQL;

/// <summary>
/// Shapes a GraphQL error and records whether retrying could help.
/// <para>
/// The correlation id is <b>not</b> added here. An error filter is built by HotChocolate's own
/// schema container during warm-up, where no request and no <c>IHttpContextAccessor</c> exist;
/// <see cref="GraphQLHttpResponseFormatter"/> adds it instead, at the HTTP boundary where the
/// request actually is.
/// </para>
/// </summary>
public class GraphQLErrorFilter : IErrorFilter
{
    public IError OnError(IError error) =>
        Shape(error).SetExtension("retryable", (error.Exception as AppGraphQLException)?.Retryable ?? false);

    private static IError Shape(IError error)
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
