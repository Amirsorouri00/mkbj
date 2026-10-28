using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Core.Implementation;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.UnitTests;

/// <summary>
/// Every response carries a correlation id (spec §6.3). An id supplied by the caller is echoed so
/// a client can tie its own logs to ours, but only if it is safe to put in a header and a log —
/// otherwise it is replaced rather than trusted.
/// </summary>
public sealed class CorrelationIdMiddlewareTests
{
    [Theory]
    [InlineData("abc-123_X.y:z", "abc-123_X.y:z")]
    public async Task Valid_Incoming_Id_Is_Echoed(string incoming, string expected)
    {
        var (context, correlation) = await RunAsync(incoming);

        context.Response.Headers[CorrelationHeaders.CorrelationId].ToString().Should().Be(expected);
        context.TraceIdentifier.Should().Be(expected);
        correlation.CorrelationId.Should().Be(expected);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("tab\there")]
    public async Task Invalid_Or_Missing_Id_Is_Replaced(string? incoming)
    {
        var (context, correlation) = await RunAsync(incoming);

        context.TraceIdentifier.Should().MatchRegex("^[0-9a-f]{32}$");
        correlation.CorrelationId.Should().Be(context.TraceIdentifier);
    }

    [Fact]
    public async Task An_Over_Long_Id_Is_Replaced()
    {
        var (context, _) = await RunAsync(new string('a', 129));

        context.TraceIdentifier.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    private static async Task<(HttpContext Context, ICorrelationContext Correlation)> RunAsync(string? incoming)
    {
        var correlation = new CorrelationContext();
        var services = new ServiceCollection();
        services.AddSingleton<ICorrelationContext>(correlation);

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };

        // DefaultHttpContext's response feature drops OnStarting callbacks, so the middleware's
        // header would never appear. This one keeps them and the test fires them, which is what a
        // real server does when the response starts.
        var responseFeature = new RecordingResponseFeature();
        context.Features.Set<IHttpResponseFeature>(responseFeature);

        if (incoming is not null) context.Request.Headers[CorrelationHeaders.CorrelationId] = incoming;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, correlation);
        await responseFeature.FireOnStartingAsync();

        return (context, correlation);
    }

    private sealed class RecordingResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));
        public void OnCompleted(Func<object, Task> callback, object state) { }

        public async Task FireOnStartingAsync()
        {
            HasStarted = true;
            foreach (var (callback, state) in _onStarting) await callback(state);
        }
    }
}
