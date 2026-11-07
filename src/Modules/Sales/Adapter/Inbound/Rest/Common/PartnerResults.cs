using System.Text.Json;
using EwaysVNext.Modules.Sales.Application;
using EwaysVNext.Outcomes;
using Microsoft.AspNetCore.Http;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Common;

/// <summary>
/// Reads and writes the partner surface's bodies in its own format.
/// </summary>
/// <remarks>
/// <para>
/// Minimal APIs bind and write with the application-wide serializer options, and the partner
/// surface is snake_case while the shop and admin surfaces are camelCase (PRD §9). So the
/// partner handlers read and write explicitly rather than inheriting a global setting that
/// another surface would have to live with.
/// </para>
/// <para>
/// <b>Error bodies are the exception and stay camelCase.</b> There is one error envelope for the
/// whole system (spec §6.3), and one shape every client can parse is worth more than matching the
/// casing of the surface it arrived on. <c>correlationId</c> is the only multi-word key in it.
/// </para>
/// </remarks>
public static class PartnerResults
{
    private const string SuccessMessage = "Success";

    public static IResult Ok<T>(T data) => new PartnerJsonResult(Envelope(data), StatusCodes.Status200OK);

    public static IResult Created<T>(string location, T data) =>
        new PartnerJsonResult(Envelope(data), StatusCodes.Status201Created, location);

    /// <summary>
    /// Reads a request body in the partner format.
    /// </summary>
    /// <remarks>
    /// Malformed JSON is a 400 with the system's error code rather than the framework's wall of
    /// text: a partner integrating against this should be told what to fix.
    /// </remarks>
    public static async Task<Result<T>> ReadAsync<T>(HttpContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(
                context.Request.Body, PartnerJson.Options, ct);

            return body is null
                ? Result.Failure<T>(SalesErrors.ValidationFailed("A request body is required."))
                : Result.Success(body);
        }
        catch (JsonException exception)
        {
            return Result.Failure<T>(SalesErrors.ValidationFailed(
                $"The request body is not valid JSON in this API's format: {exception.Message}"));
        }
    }

    private static Response<T> Envelope<T>(T data) => new(SuccessMessage, data);

    private sealed record Response<T>(string Message, T Data);

    private sealed class PartnerJsonResult(object body, int statusCode, string? location = null) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json; charset=utf-8";

            if (location is not null) httpContext.Response.Headers.Location = location;

            return httpContext.Response.WriteAsync(
                JsonSerializer.Serialize(body, PartnerJson.Options));
        }
    }
}
