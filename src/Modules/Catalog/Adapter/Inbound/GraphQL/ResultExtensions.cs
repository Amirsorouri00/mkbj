using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL;

// Thin, name-compatible wrapper around the shared ResultGraphQLExtensions so every
// resolver call site here keeps working unchanged, while still throwing the same
// AppGraphQLException-derived exceptions the rest of the app relies on (this is what
// lets GraphQLErrorFilter preserve the original Error.Code instead of masking it).
internal static class ResultExtensions
{
    public static T ThrowIfFailure<T>(this Outcomes.Result<T> result) =>
        result.IsSuccess ? result.Value! : throw result.Error!.ToGraphQLException();

    public static void ThrowIfFailure(this Result result)
    {
        if (result.IsFailure) throw result.Error!.ToGraphQLException();
    }
}
