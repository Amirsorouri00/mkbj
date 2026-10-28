using System.Text.Json;
using System.Text.Json.Serialization;
using EwaysVNext.Outcomes;
using Microsoft.AspNetCore.Http;

namespace EwaysVNext.Presentation.Endpoint;

/// <summary>
/// The one error shape every REST response uses (spec §6.3). <c>details</c> and <c>items</c> are
/// omitted entirely when absent rather than sent as null, so a client can treat their presence as
/// meaningful.
/// </summary>
public sealed record ErrorResponse(
    string Code,
    string Message,
    bool Retryable,
    string CorrelationId,
    object? Details = null,
    object? Items = null)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ErrorResponse From(Error error, HttpContext context, object? details = null, object? items = null) =>
        new(error.Code, error.Message, error.Retryable, context.TraceIdentifier, details, items);

    public static Task WriteAsync(
        HttpContext context, Error error, object? details = null, object? items = null)
    {
        context.Response.StatusCode = error.HttpStatus;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(From(error, context, details, items), JsonOptions));
    }
}
