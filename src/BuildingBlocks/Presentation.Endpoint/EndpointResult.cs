using Microsoft.AspNetCore.Http;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Presentation.Endpoint;

public static class EndpointResult
{
    internal static IResult Succeed<TData>(TData data) =>
        Results.Ok(new { Message = EndpointResultMessages.Success, Data = data });

    internal static IResult Created<TData>(string location, TData data) =>
        Results.Created(location, new { Message = EndpointResultMessages.Success, Data = data });

    /// <summary>
    /// The single place a REST error body is produced (spec §6.3). Public because callers outside
    /// the Result pipeline need the same shape — a rejected rate limit, a GraphQL filter, a
    /// PRICE_CHANGED that carries the fresh cart in <paramref name="details"/>.
    /// </summary>
    public static IResult Fail(Error error, object? details = null, object? items = null) =>
        new ErrorBodyResult(error, details, items);

    private sealed class ErrorBodyResult(Error error, object? details, object? items) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) =>
            ErrorResponse.WriteAsync(httpContext, error, details, items);
    }

    public static IResult Unauthorized(string? message = null) =>
        Fail(message is null ? Error.Unauthorized : Error.Unauthorized with { Message = message });

    public static IResult Forbidden(string? message = null) =>
        Fail(message is null ? Error.Forbidden : Error.Forbidden with { Message = message });

    public static IResult BadRequest(string? message = null) =>
        Fail(message is null ? Error.Validation : Error.Validation with { Message = message });

    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess
            ? Results.Ok(new { Message = EndpointResultMessages.Success })
            : Fail(result.Error!);

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? Succeed(result.Value) : Fail(result.Error!);

    public static IResult ToHttpResult<T, TResponse>(this Result<T> result, Func<T, TResponse> map) =>
        result.IsSuccess ? Succeed(map(result.Value!)) : Fail(result.Error!);

    public static IResult ToHttpResult<TPayload>(this Result result, TPayload payload) =>
        result.IsSuccess ? Succeed(payload) : Fail(result.Error!);

    public static IResult ToCreatedHttpResult<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? Created(location(result.Value!), result.Value) : Fail(result.Error!);

    public static IResult ToCreatedHttpResult<TPayload>(this Result result, string location, TPayload payload) =>
        result.IsSuccess ? Created(location, payload) : Fail(result.Error!);
}
