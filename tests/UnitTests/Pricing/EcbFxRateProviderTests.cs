using System.Net;
using System.Text;
using AwesomeAssertions;
using EwaysVNext.Modules.Pricing.Adapter.Outbound.Fx.Ecb;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EwaysVNext.UnitTests.Pricing;

/// <summary>
/// Reading the ECB's daily file. No test here touches the network: the fixture is a copy of the
/// real format with fixed values, so a published rate changing cannot turn these red.
/// </summary>
public sealed class EcbFxRateProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EcbFxRateProvider Provider(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://rates.invalid/stats/eurofxref/") },
            NullLogger<EcbFxRateProvider>.Instance);

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Pricing", "Fixtures", "eurofxref-daily-2026-10-27.xml"));

    [Fact]
    public async Task Parses_Fixture_Rates_And_Date()
    {
        var batch = (await Provider(new StubHandler(HttpStatusCode.OK, Fixture())).FetchAsync(Ct)).Value!;

        batch.Provider.Should().Be("ecb");
        batch.RateDate.Should().Be(new DateOnly(2026, 10, 27),
            "a rate belongs to the day it was published for, not the day we asked");
        batch.Rates.Should().HaveCount(4);
        batch.Rates.Should().ContainEquivalentOf(
            new EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound.FxQuote("USD", 1.0812m));
        batch.Rates.Should().ContainEquivalentOf(
            new EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound.FxQuote("GBP", 0.84215m));
    }

    [Fact]
    public async Task Http_500_Is_Retryable_Provider_Failure()
    {
        var result = await Provider(new StubHandler(HttpStatusCode.InternalServerError, "")).FetchAsync(Ct);

        result.Error!.Code.Should().Be("FX_PROVIDER_FAILED");
        result.Error.HttpStatus.Should().Be(502, "the failure is upstream, not ours");
        result.Error.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task Malformed_Xml_Is_Provider_Failure()
    {
        var result = await Provider(new StubHandler(HttpStatusCode.OK, "<not-xml")).FetchAsync(Ct);

        result.Error!.Code.Should().Be("FX_PROVIDER_FAILED",
            "network trouble and a broken document are the same thing to a caller");
    }

    [Fact]
    public async Task A_Document_With_No_Date_Is_A_Provider_Failure()
    {
        const string noDate = """
            <?xml version="1.0" encoding="UTF-8"?>
            <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
              <Cube><Cube><Cube currency="USD" rate="1.0812"/></Cube></Cube>
            </gesmes:Envelope>
            """;

        (await Provider(new StubHandler(HttpStatusCode.OK, noDate)).FetchAsync(Ct))
            .Error!.Code.Should().Be("FX_PROVIDER_FAILED");
    }

    [Fact]
    public async Task A_Day_With_No_Rates_Is_A_Provider_Failure()
    {
        const string empty = """
            <?xml version="1.0" encoding="UTF-8"?>
            <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
              <Cube><Cube time="2026-10-27"></Cube></Cube>
            </gesmes:Envelope>
            """;

        (await Provider(new StubHandler(HttpStatusCode.OK, empty)).FetchAsync(Ct))
            .Error!.Code.Should().Be("FX_PROVIDER_FAILED",
                "an empty day is not a day with no movement; it is a document we cannot use");
    }

    [Fact]
    public async Task A_Single_Unreadable_Rate_Does_Not_Cost_The_Rest()
    {
        const string oneBad = """
            <?xml version="1.0" encoding="UTF-8"?>
            <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
              <Cube><Cube time="2026-10-27">
                <Cube currency="USD" rate="1.0812"/>
                <Cube currency="XXX" rate="not-a-number"/>
                <Cube currency="SGD" rate="1.4321"/>
              </Cube></Cube>
            </gesmes:Envelope>
            """;

        var batch = (await Provider(new StubHandler(HttpStatusCode.OK, oneBad)).FetchAsync(Ct)).Value!;

        batch.Rates.Select(quote => quote.Quote).Should().BeEquivalentTo(["USD", "SGD"]);
    }

    [Fact]
    public async Task Rates_Are_Read_Under_The_Invariant_Culture()
    {
        // The document writes 1.0812 with a dot. Under a comma-decimal culture a careless parse
        // reads that as ten thousand eight hundred and twelve.
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

        try
        {
            var batch = (await Provider(new StubHandler(HttpStatusCode.OK, Fixture())).FetchAsync(Ct)).Value!;

            batch.Rates.Single(quote => quote.Quote == "USD").Rate.Should().Be(1.0812m);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task A_Network_Failure_Is_Reported_Rather_Than_Thrown()
    {
        var result = await Provider(new ThrowingHandler()).FetchAsync(Ct);

        result.Error!.Code.Should().Be("FX_PROVIDER_FAILED");
        result.Error.Retryable.Should().BeTrue();
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml"),
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("the network is down");
    }
}
