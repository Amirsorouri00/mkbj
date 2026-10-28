using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EwaysVNext.Modules.Payment.GatewayTests;

public sealed class ParsianInquiryTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 29, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Report_request_uses_the_documented_headers_path_and_casing()
    {
        using var bank = new BankFixture((_, _) => Reply(SuccessData(RecordObject())));
        var request = Request();

        var result = await bank.Client.GetSaleReportAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        var sent = Assert.Single(bank.Handler.Sent);
        Assert.Equal("https://pgwservices.pec.ir/api/PGWReport/GetSaleReport", sent.Url);
        Assert.Equal("Bearer", sent.Authorization?.Scheme);
        Assert.Equal("token-1", sent.Authorization?.Parameter);
        Assert.Equal("1.0", sent.ApiVersion);
        Assert.Equal("application/json", sent.ContentType);
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal(456, body.RootElement.GetProperty("RRN").GetInt64());
        Assert.Equal(1001, body.RootElement.GetProperty("OrderId").GetInt64());
        Assert.Equal(123, body.RootElement.GetProperty("Token").GetInt64());
        var from = body.RootElement.GetProperty("FromDate").GetDateTime();
        var to = body.RootElement.GetProperty("ToDate").GetDateTime();
        Assert.Equal(DateTimeKind.Unspecified, from.Kind);
        Assert.Equal(new DateTime(2026, 9, 29), from);
        Assert.Equal(new DateTime(2026, 9, 29).AddDays(1).AddTicks(-1), to);
    }

    [Fact]
    public async Task Token_failure_makes_no_HTTP_call()
    {
        var tokens = new FakeTokenProvider { GetResult = Result.Failure<string>(new Error("TOKEN_DOWN", "down")) };
        using var bank = new BankFixture((_, _) => Reply(SuccessData(RecordObject())), tokens);

        var result = await bank.Client.GetSaleReportAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal("TOKEN_DOWN", result.Error!.Code);
        Assert.Empty(bank.Handler.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Invalid_successful_token_makes_no_HTTP_call(string? token)
    {
        var tokens = new FakeTokenProvider { GetResult = Result.Success(token!) };
        using var bank = new BankFixture((_, _) => Reply(SuccessData(RecordObject())), tokens);

        var result = await bank.Client.GetSaleReportAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal("PARSIAN_INQUIRY_TOKEN_INVALID", result.Error!.Code);
        Assert.Empty(bank.Handler.Sent);
    }

    [Fact]
    public async Task One_401_renews_the_rejected_token_and_retries_once()
    {
        var tokens = new FakeTokenProvider();
        using var bank = new BankFixture((attempt, _) => attempt == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : Reply(SuccessData(RecordObject())), tokens);

        var result = await bank.Client.GetSaleReportAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(["token-1"], tokens.RejectedTokens);
        Assert.Equal(["token-1", "token-2"], bank.Handler.Sent.Select(x => x.Authorization!.Parameter));
    }

    [Fact]
    public async Task A_second_401_stops_without_another_renewal()
    {
        var tokens = new FakeTokenProvider();
        using var bank = new BankFixture((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized), tokens);

        var result = await bank.Client.GetSaleReportAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal("PARSIAN_INQUIRY_AUTHORIZATION_REJECTED", result.Error!.Code);
        Assert.DoesNotContain("token-1", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("token-2", result.Error.Message, StringComparison.Ordinal);
        Assert.Single(tokens.RejectedTokens);
        Assert.Equal(2, bank.Handler.Sent.Count);
    }

    [Fact]
    public async Task Transport_retry_is_bounded_across_one_authorization_renewal()
    {
        var tokens = new FakeTokenProvider();
        using var bank = new BankFixture((attempt, _) => attempt switch
        {
            1 or 3 => throw new HttpRequestException("transient"),
            2 => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => Reply(SuccessData(RecordObject()))
        }, tokens);

        var result = await bank.Client.GetSaleReportAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Single(tokens.RejectedTokens);
        Assert.Equal(4, bank.Handler.Sent.Count);
    }

    [Fact]
    public async Task Failed_renewal_stops_before_a_second_report_call()
    {
        var tokens = new FakeTokenProvider
        {
            RenewResult = Result.Failure<string>(new Error("RENEW_DOWN", "down"))
        };
        using var bank = new BankFixture((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized), tokens);

        var result = await bank.Client.GetSaleReportAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal("RENEW_DOWN", result.Error!.Code);
        Assert.Single(bank.Handler.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Data_accepts_one_record_object_or_an_array(bool array)
    {
        var data = array ? "[" + RecordObject() + "]" : RecordObject();
        using var bank = new BankFixture((_, _) => Reply(SuccessData(data)));

        var result = await bank.Client.GetSaleReportAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Single(result.Value!.Records);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("\"text\"")]
    [InlineData("{\"Items\":[]}")]
    public async Task Null_primitive_and_unknown_wrapper_data_are_invalid(string data)
    {
        using var bank = new BankFixture((_, _) => Reply(SuccessData(data)));

        var result = await bank.Client.GetSaleReportAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentGatewayErrors.InvalidResponse, result.Error);
    }

    [Fact]
    public async Task Provider_returns_the_correlated_amount_and_raw_PGW_status()
    {
        using var bank = new BankFixture((_, _) => Reply(SuccessData(RecordObject(amount: 2500, pgwStatus: 17))));

        var result = await bank.Inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(2500, result.Value!.BankAmount.Amount);
        Assert.Equal("456", result.Value.ProviderReference);
        Assert.Equal("17", result.Value.ProviderStatus);
        Assert.Equal("0", result.Value.ProviderCode);
    }

    [Theory]
    [InlineData("not-a-reference", "123")]
    [InlineData("456", "not-a-token")]
    public async Task Invalid_retained_identifiers_fail_before_token_or_HTTP(string reference, string token)
    {
        var tokens = new FakeTokenProvider();
        using var bank = new BankFixture((_, _) => Reply(SuccessData(RecordObject())), tokens);

        var result = await bank.Inquiry.InquiryAsync(Context(reference, token), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentGatewayErrors.InvalidContext, result.Error);
        Assert.Equal(0, tokens.GetCalls);
        Assert.Empty(bank.Handler.Sent);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_before_HTTP()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var bank = new BankFixture((_, _) => Reply(SuccessData(RecordObject())));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => bank.Client.GetSaleReportAsync(Request(), cancelled.Token));

        Assert.Empty(bank.Handler.Sent);
    }

    [Fact]
    public async Task Empty_report_maps_to_transaction_not_found()
    {
        using var bank = new BankFixture((_, _) => Reply(SuccessData("[]")));

        var result = await bank.Inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentGatewayErrors.TransactionNotFound, result.Error);
    }

    [Theory]
    [InlineData("[{\"Amount\":1000,\"OrderId\":1001,\"Token\":123,\"RRN\":999}]")]
    [InlineData("[{\"Amount\":0,\"OrderId\":1001,\"Token\":123,\"RRN\":456}]")]
    [InlineData("[{\"Amount\":1000,\"OrderId\":1001,\"Token\":123,\"RRN\":456},{\"Amount\":1000,\"OrderId\":1001,\"Token\":123,\"RRN\":456}]")]
    public async Task Mismatched_zero_or_duplicate_records_are_invalid(string data)
    {
        using var bank = new BankFixture((_, _) => Reply(SuccessData(data)));

        var result = await bank.Inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentGatewayErrors.InvalidResponse, result.Error);
    }

    [Theory]
    [InlineData(-8, "PARSIAN_REPORT_STATUS_-8")]
    [InlineData(-99, "PARSIAN_REPORT_STATUS_-99")]
    public async Task Report_status_is_mapped_without_inventing_a_payment_state(short status, string code)
    {
        using var bank = new BankFixture((_, _) => Reply($$"""{"Status":{{status}},"Message":"refused","Data":null}"""));

        var result = await bank.Inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Equal(code, result.Error!.Code);
    }

    [Fact]
    public async Task Missing_token_backend_fails_before_HTTP_but_existing_provider_still_resolves()
    {
        using var bank = new BankFixture((_, _) => Reply(SuccessData(RecordObject())), registerTokens: false);

        var result = await bank.Inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Equal("PARSIAN_INQUIRY_TOKEN_PROVIDER_UNAVAILABLE", result.Error!.Code);
        Assert.Empty(bank.Handler.Sent);
        Assert.Equal("parsian", bank.Provider.Name);
    }

    [Theory]
    [InlineData("BaseUrl", "http://unsafe.test")]
    [InlineData("ApiVersion", "")]
    [InlineData("RequestTimeout", "00:00:00")]
    public void Invalid_report_configuration_fails_startup_validation(string key, string value)
    {
        var values = new Dictionary<string, string?>
        {
            ["Payment:Gateways:Parsian:Enabled"] = "true",
            ["Payment:Gateways:Parsian:LoginAccount"] = "test-only-credential",
            ["Payment:Gateways:Parsian:Inquiry:" + key] = value
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentModule(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    private static SaleReportRequest Request() => new(456, 1001, 123,
        new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Unspecified),
        new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Unspecified).AddDays(1).AddTicks(-1));

    private static GatewayPaymentContext Context(string reference = "456", string token = "123") =>
        new(42, Money.Create(1000), GatewayAttempt.Create("parsian", 1001, token, RequestedAt, reference));

    private static string RecordObject(long amount = 1000, short pgwStatus = 0) => $$"""
        {"Amount":{{amount}},"OrderId":1001,"Token":123,"RRN":456,"RequestDateTime":"2026-09-29T12:30:00","PGWStatusId":{{pgwStatus}}}
        """;

    private static string SuccessData(string data) => $$"""{"Status":0,"Message":"","Data":{{data}}}""";

    private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:Gateways:Parsian:Enabled"] = "true",
            ["Payment:Gateways:Parsian:LoginAccount"] = "test-only-credential"
        }).Build();

    private sealed class BankFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public RecordingHandler Handler { get; }
        public FakeTokenProvider Tokens { get; }
        public ParsianSaleReportHttpClient Client => _services.GetRequiredService<ParsianSaleReportHttpClient>();
        public IPaymentGatewayProvider Provider { get; }
        public IPaymentInquiryProvider Inquiry => (IPaymentInquiryProvider)Provider;

        public BankFixture(Func<int, CancellationToken, HttpResponseMessage> response,
            FakeTokenProvider? tokens = null, bool registerTokens = true)
        {
            Handler = new(response);
            Tokens = tokens ?? new FakeTokenProvider();
            var services = new ServiceCollection();
            services.AddLogging();
            if (registerTokens)
                services.AddSingleton<IParsianInquiryAccessTokenProvider>(Tokens);
            services.AddPaymentModule(Configuration());
            services.AddHttpClient(ParsianRegistration.InquiryHttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Handler);
            _services = services.BuildServiceProvider();
            Provider = _services.GetRequiredService<IPaymentGatewayProviderSelector>()
                .SelectRetained("parsian").Value!;
        }

        public void Dispose()
        {
            _services.Dispose();
            Handler.Dispose();
        }
    }

    private sealed class FakeTokenProvider : IParsianInquiryAccessTokenProvider
    {
        public Result<string> GetResult { get; init; } = Result.Success("token-1");
        public Result<string> RenewResult { get; init; } = Result.Success("token-2");
        public List<string> RejectedTokens { get; } = [];
        public int GetCalls { get; private set; }

        public Task<Result<string>> GetValidTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetCalls++;
            return Task.FromResult(GetResult);
        }

        public Task<Result<string>> RenewRejectedTokenAsync(string rejectedToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectedTokens.Add(rejectedToken);
            return Task.FromResult(RenewResult);
        }
    }

    private sealed class RecordingHandler(Func<int, CancellationToken, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<SentRequest> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add(new(request.RequestUri!.AbsoluteUri,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization,
                request.Headers.TryGetValues("apiVersion", out var values) ? values.Single() : null,
                request.Content?.Headers.ContentType?.MediaType));
            return response(Sent.Count, cancellationToken);
        }
    }

    private sealed record SentRequest(string Url, string Body, AuthenticationHeaderValue? Authorization,
        string? ApiVersion, string? ContentType);
}
