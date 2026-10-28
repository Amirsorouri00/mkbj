using System.Net;
using HotChocolate.AspNetCore.Formatters;
using HotChocolate.Execution;

namespace EwaysVNext.Presentation.Endpoint.GraphQL;

public sealed class GraphQLHttpResponseFormatter : DefaultHttpResponseFormatter
{
    protected override HttpStatusCode OnDetermineStatusCode(
        OperationResult result, FormatInfo format, HttpStatusCode? proposedStatusCode)
    {
        if (result.Errors.Any(e => e.Code == "AUTH_NOT_AUTHENTICATED"))
            return HttpStatusCode.Unauthorized;

        if (result.Errors.Any(e => e.Code == "AUTH_NOT_AUTHORIZED"))
            return HttpStatusCode.Forbidden;

        return base.OnDetermineStatusCode(result, format, proposedStatusCode);
    }
}
