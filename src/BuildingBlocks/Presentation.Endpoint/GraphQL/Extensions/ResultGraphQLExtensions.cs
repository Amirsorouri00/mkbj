using EwaysVNext.Outcomes;

namespace EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;

public static class ResultGraphQLExtensions
{
    public static T ValueOrThrow<T>(this Result<T> result) =>
        result.IsSuccess ? result.Value! : throw result.Error!.ToGraphQLException();

    public static void ThrowIfFailure(this Result result)
    {
        if (result.IsFailure) throw result.Error!.ToGraphQLException();
    }

    public static TPayload ValueOrThrow<TPayload>(this Result result, TPayload payload) =>
        result.IsSuccess ? payload : throw result.Error!.ToGraphQLException();

    public static T? ValueOrDefault<T>(this Result<T> result) where T : class
    {
        if (result.IsSuccess) return result.Value;
        if (result.Error!.HttpStatus == 404) return null;
        throw result.Error.ToGraphQLException();
    }
}
