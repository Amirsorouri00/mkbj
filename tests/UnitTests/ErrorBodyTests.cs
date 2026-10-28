using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.UnitTests;

/// <summary>
/// Every error a caller sees has the same shape (spec §6.3), carries whether retrying could help,
/// and carries the correlation id needed to find the request in the logs.
/// </summary>
public sealed class ErrorBodyTests
{
    [Fact]
    public void Retryable_Defaults_To_False() =>
        new Error("X_NOT_FOUND", "x").Retryable.Should().BeFalse();

    [Fact]
    public void Retryable_Presets()
    {
        Error.RateLimited.Should().Be(new Error("RATE_LIMITED", Error.RateLimited.Message, 429, true));
        Error.ServiceUnavailable.Should().Be(new Error("SERVICE_UNAVAILABLE", Error.ServiceUnavailable.Message, 503, true));
        Error.Validation.Code.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Fail_Writes_The_Standard_Body()
    {
        var context = NewContext();

        await EndpointResult.Fail(new Error("PRICE_CHANGED", "Prices changed.")).ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(400);
        Json(context).Should().Be(
            """{"code":"PRICE_CHANGED","message":"Prices changed.","retryable":false,"correlationId":"corr-1"}""");
    }

    [Fact]
    public async Task Fail_Includes_Details_And_Items_Only_When_Given()
    {
        var withoutExtras = NewContext();
        await EndpointResult.Fail(Error.Validation).ExecuteAsync(withoutExtras);
        Json(withoutExtras).Should().NotContain("details").And.NotContain("items");

        var withExtras = NewContext();
        await EndpointResult.Fail(Error.Validation, details: new { field = "amount" }, items: new[] { 1 })
            .ExecuteAsync(withExtras);
        Json(withExtras).Should().Contain("\"details\":{\"field\":\"amount\"}").And.Contain("\"items\":[1]");
    }

    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddRouting();

        return new DefaultHttpContext
        {
            TraceIdentifier = "corr-1",
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
    }

    private static string Json(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEnd();
    }
}
