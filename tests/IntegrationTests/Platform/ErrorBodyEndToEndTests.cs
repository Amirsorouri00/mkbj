using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.IntegrationTests.Platform;

/// <summary>
/// The error contract as a caller actually receives it: a correlation id on every response, and a
/// rate-limit rejection — which never reaches a handler — shaped like every other error.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class ErrorBodyEndToEndTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Every_Response_Has_A_Correlation_Header()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.Headers.GetValues("X-Correlation-Id").Single().Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_Supplied_Correlation_Id_Is_Echoed()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "caller-supplied-1");

        var response = await client.GetAsync("/health/live");

        response.Headers.GetValues("X-Correlation-Id").Single().Should().Be("caller-supplied-1");
    }

    [Fact]
    public async Task Rate_Limited_Response_Uses_The_Error_Body()
    {
        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            // Overrides the fixture's deliberately huge default. The suite raises the anonymous
            // limit so that no other test is accidentally measuring the limiter; this one is
            // about the limiter, so it sets its own.
            ["RateLimiting:AnonymousPermitPerMinute"] = "2",
        });
        using var client = factory.CreateClient();

        await client.GetAsync("/health/live");
        await client.GetAsync("/health/live");
        var rejected = await client.GetAsync("/health/live");

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(60));

        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be("RATE_LIMITED");
        body.RootElement.GetProperty("retryable").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("correlationId").GetString()
            .Should().Be(rejected.Headers.GetValues("X-Correlation-Id").Single());
    }
}
