using System.Net;
using System.Text;
using System.Text.Json;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.GatewayTests;

public sealed class ParsianTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 29, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sale_maps_trusted_attempt_to_json_and_returns_get_handoff()
    {
        using var bank = new BankFixture((_, _) => Reply("""{"Status":0,"Message":"ok","Token":123}"""));
        var result = await bank.Provider.CreateLinkAsync(LinkRequest(), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal("123", result.Value!.Token);
        Assert.Equal("https://pec.shaparak.ir/NewIPG/?token=123", result.Value.Redirect.Url.AbsoluteUri);
        Assert.Equal(GatewayRedirectMethod.Get, result.Value.Redirect.Method);
        Assert.Empty(result.Value.Redirect.Fields);
        var sent = Assert.Single(bank.Handler.Sent);
        Assert.Equal("https://pec.shaparak.ir/NewIPGRestServices/Payment/SalePaymentRequest", sent.Url);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("application/json", sent.ContentType);
        Assert.Null(bank.Handler.Requests.Single().Headers.Authorization);
        Assert.False(bank.Handler.Requests.Single().Headers.Contains("apiVersion"));
        using var json = JsonDocument.Parse(sent.Body);
        Assert.Equal("test-only-credential", json.RootElement.GetProperty("LoginAccount").GetString());
        Assert.Equal(1001, json.RootElement.GetProperty("OrderId").GetInt64());
        Assert.Equal(1000, json.RootElement.GetProperty("Amount").GetInt64());
        Assert.Equal("https://merchant.test/callback", json.RootElement.GetProperty("CallBackUrl").GetString());
        Assert.False(json.RootElement.TryGetProperty("requestData", out _));
    }

    [Theory]
    [InlineData("999")]
    [InlineData("1000.50")]
    [InlineData("9223372036854775808")]
    public async Task Invalid_bank_amount_is_refused_without_http(string amount)
    {
        using var bank = new BankFixture((_, _) => Reply("{}"));
        var request = LinkRequest() with { Amount = Money.Create(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)) };
        Assert.Equal(PaymentGatewayErrors.UnsupportedAmount, (await bank.Provider.CreateLinkAsync(request, TestContext.Current.CancellationToken)).Error);
        Assert.Empty(bank.Handler.Sent);
    }

    [Theory]
    [InlineData("http://merchant.test/callback", 1001, "PAYMENT_CALLBACK_UNSUPPORTED")]
    [InlineData("https://merchant.test/callback", 0, "PAYMENT_REQUEST_INVALID")]
    public async Task Invalid_sale_identity_or_callback_is_refused(string callback, long requestId, string code)
    {
        using var bank = new BankFixture((_, _) => Reply("{}"));
        var result = await bank.Provider.CreateLinkAsync(LinkRequest() with { CallbackUrl = new Uri(callback), RequestId = requestId }, TestContext.Current.CancellationToken);
        Assert.Equal(code, result.Error!.Code);
        Assert.Empty(bank.Handler.Sent);
    }

    [Theory]
    [InlineData(-112)]
    [InlineData(-126)]
    [InlineData(-999)]
    public async Task Sale_rejection_preserves_provider_status_and_message(int status)
    {
        using var bank = new BankFixture((_, _) => Reply($$"""{"Status":{{status}},"Message":"bank detail","Token":0}"""));
        var result = await bank.Provider.CreateLinkAsync(LinkRequest(), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal($"PARSIAN_STATUS_{status}", result.Error!.Code);
        Assert.Contains("bank detail", result.Error.Message);
        Assert.Single(bank.Handler.Sent);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Token\":123}")]
    [InlineData("{\"Status\":0,\"Token\":0}")]
    [InlineData("{\"Status\":0,\"Token\":-1}")]
    [InlineData("{\"status\":0,\"Token\":123}")]
    [InlineData("{\"Status\":\"bad\",\"Token\":123}")]
    [InlineData("null")]
    [InlineData("not json")]
    public async Task Malformed_sale_is_not_success(string json)
    {
        using var bank = new BankFixture((_, _) => Reply(json));
        Assert.Equal(PaymentGatewayErrors.InvalidResponse, (await bank.Provider.CreateLinkAsync(LinkRequest(), TestContext.Current.CancellationToken)).Error);
    }

    [Theory]
    [InlineData("-138", "Failed")]
    [InlineData("-999", "Unknown")]
    public async Task Unpaid_callback_is_normalized_without_contacting_the_bank(string status, string state)
    {
        using var bank = new BankFixture((_, _) => Reply(Success));
        var fields = new Dictionary<string, string>
        {
            ["status"] = status, ["Amount"] = "999999", ["Token"] = "attacker-token", ["OrderId"] = "999"
        };
        var result = await bank.Provider.VerifyCallbackAsync(new(Context(), fields), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(state, result.Value!.State.ToString());
        Assert.Equal(status, result.Value.ProviderCode);
        Assert.Null(result.Value.ProviderReference);
        Assert.Equal(1001, result.Value.RequestId);
        Assert.Equal("123", result.Value.Token);
        Assert.Empty(bank.Handler.Sent);
    }

    [Theory]
    [InlineData("Status", "0", "RRN", "456")]
    [InlineData("status", "0", "rrn", "456")]
    [InlineData("status", "0", "RRN", "0")]
    [InlineData("status", "0", "RRN", "invalid")]
    [InlineData("status", "invalid", "RRN", "456")]
    public async Task Invalid_callback_evidence_is_refused_without_confirming(string key, string status, string rrnKey, string rrn)
    {
        using var bank = new BankFixture((_, _) => Reply(Success));
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [key] = status, [rrnKey] = rrn };
        Assert.Equal(PaymentGatewayErrors.InvalidCallback,
            (await bank.Provider.VerifyCallbackAsync(new(Context(), fields), TestContext.Current.CancellationToken)).Error);
        Assert.Empty(bank.Handler.Sent);
    }

    [Fact]
    public async Task Paid_callback_is_confirmed_with_the_bank_using_the_trusted_token()
    {
        using var bank = new BankFixture((_, _) => Reply(Success));
        var fields = new Dictionary<string, string> { ["status"] = "0", ["RRN"] = "456", ["Token"] = "999" };
        var result = await bank.Provider.VerifyCallbackAsync(new(Context(), fields), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(GatewayPaymentState.Confirmed, result.Value!.State);
        Assert.Equal("456", result.Value.ProviderReference);
        Assert.Equal("0", result.Value.ProviderCode);
        var sent = Assert.Single(bank.Handler.Sent);
        Assert.Equal("https://pec.shaparak.ir/NewIPGRestServices/payment/ConfirmPayment", sent.Url);
        using var json = JsonDocument.Parse(sent.Body);
        Assert.Equal(123, json.RootElement.GetProperty("Token").GetInt64());
        Assert.Equal("test-only-credential", json.RootElement.GetProperty("LoginAccount").GetString());
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task Callback_rrn_is_compared_with_the_confirmation_as_a_number()
    {
        using var bank = new BankFixture((_, _) => Reply(Success));
        var result = await bank.Provider.VerifyCallbackAsync(new(Context(), PaidFields("000456")), TestContext.Current.CancellationToken);
        Assert.Equal(GatewayPaymentState.Confirmed, result.Value!.State);
        Assert.Equal("456", result.Value.ProviderReference);
    }

    [Theory]
    [InlineData(-126, "Failed")]
    [InlineData(-1528, "Failed")]
    [InlineData(-999, "Unknown")]
    public async Task Confirmation_refusal_is_failed_only_for_known_codes(int status, string state)
    {
        using var bank = new BankFixture((_, _) => Reply($$"""{"Status":{{status}},"Token":123}"""));
        var result = await bank.Provider.VerifyCallbackAsync(new(Context(), PaidFields()), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(state, result.Value!.State.ToString());
        Assert.Equal(status.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Value.ProviderCode);
        Assert.Equal("456", result.Value.ProviderReference);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Token\":123,\"RRN\":456}")]
    [InlineData("{\"Status\":0,\"Token\":999,\"RRN\":456}")]
    [InlineData("{\"Status\":0,\"Token\":123,\"RRN\":0}")]
    [InlineData("{\"Status\":0,\"Token\":123,\"RRN\":999}")]
    [InlineData("null")]
    [InlineData("not json")]
    public async Task Malformed_or_mismatched_confirmation_is_unknown_never_confirmed(string json)
    {
        using var bank = new BankFixture((_, _) => Reply(json));
        var result = await bank.Provider.VerifyCallbackAsync(new(Context(), PaidFields()), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(GatewayPaymentState.Unknown, result.Value!.State);
    }

    [Fact]
    public async Task Confirmation_http_error_is_unknown_not_a_bank_status()
    {
        using var bank = new BankFixture((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(Success) });
        var result = await bank.Provider.VerifyCallbackAsync(new(Context(), PaidFields()), TestContext.Current.CancellationToken);
        Assert.Equal(GatewayPaymentState.Unknown, result.Value!.State);
        Assert.Null(result.Value.ProviderCode);
        Assert.Single(bank.Handler.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmation_transport_retry_uses_a_fresh_request_then_confirms_or_is_unknown(bool exhaust)
    {
        using var bank = new BankFixture((attempt, _) =>
            attempt == 1 || exhaust ? throw new HttpRequestException("connection lost") : Reply(Success));
        var result = await bank.Provider.VerifyCallbackAsync(new(Context(), PaidFields()), TestContext.Current.CancellationToken);
        Assert.Equal(2, bank.Handler.Sent.Count);
        Assert.NotSame(bank.Handler.Requests[0], bank.Handler.Requests[1]);
        Assert.Equal(bank.Handler.Sent[0].Body, bank.Handler.Sent[1].Body);
        Assert.Equal(exhaust ? GatewayPaymentState.Unknown : GatewayPaymentState.Confirmed, result.Value!.State);
    }

    [Fact]
    public async Task Confirmation_timeout_is_retried_then_unknown()
    {
        using var bank = new BankFixture((_, _) => throw new TaskCanceledException("http timeout"));
        var result = await bank.Provider.VerifyCallbackAsync(new(Context(), PaidFields()), TestContext.Current.CancellationToken);
        Assert.Equal(GatewayPaymentState.Unknown, result.Value!.State);
        Assert.Equal(2, bank.Handler.Sent.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Caller_cancellation_during_confirmation_propagates_without_retry(int cancelAttempt)
    {
        using var cancellation = new CancellationTokenSource();
        using var bank = new BankFixture((attempt, ct) =>
        {
            if (attempt < cancelAttempt) throw new HttpRequestException("first attempt lost");
            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            return Reply(Success);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bank.Provider.VerifyCallbackAsync(new(Context(), PaidFields()), cancellation.Token));
        Assert.Equal(cancelAttempt, bank.Handler.Sent.Count);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-token")]
    [InlineData("9223372036854775808")]
    public async Task Invalid_trusted_token_blocks_confirmation_and_reversal(string token)
    {
        using var bank = new BankFixture((_, _) => Reply(Success));
        Assert.Equal(PaymentGatewayErrors.InvalidContext,
            (await bank.Provider.VerifyCallbackAsync(new(Context(token: token), PaidFields()), TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PaymentGatewayErrors.InvalidContext,
            (await bank.Provider.ReverseAsync(Context(token: token), TestContext.Current.CancellationToken)).Error);
        Assert.Empty(bank.Handler.Sent);
    }

    [Fact]
    public async Task Another_providers_context_is_refused_without_http()
    {
        using var bank = new BankFixture((_, _) => Reply(Success));
        Assert.Equal(PaymentGatewayErrors.InvalidContext,
            (await bank.Provider.VerifyCallbackAsync(new(Context(provider: "mellat"), PaidFields()), TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PaymentGatewayErrors.InvalidContext,
            (await bank.Provider.ReverseAsync(Context(provider: "mellat"), TestContext.Current.CancellationToken)).Error);
        Assert.Empty(bank.Handler.Sent);
    }

    [Fact]
    public async Task Inquiry_is_offered_but_safe_without_a_token_backend()
    {
        using var bank = new BankFixture((_, _) => Reply(Success));
        var inquiry = Assert.IsAssignableFrom<IPaymentInquiryProvider>(bank.Provider);
        var result = await inquiry.InquiryAsync(Context(reference: "456"), TestContext.Current.CancellationToken);
        Assert.Equal("PARSIAN_INQUIRY_TOKEN_PROVIDER_UNAVAILABLE", result.Error!.Code);
        Assert.Empty(bank.Handler.Sent);
    }

    [Fact]
    public async Task Reversal_uses_trusted_token_and_exact_endpoint()
    {
        using var bank = new BankFixture((_, _) => Reply(Success));
        var result = await bank.Provider.ReverseAsync(Context(), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(GatewayReversalState.Reversed, result.Value!.State);
        Assert.Equal("0", result.Value.ProviderCode);
        var sent = Assert.Single(bank.Handler.Sent);
        Assert.Equal("https://pec.shaparak.ir/NewIPGRestServices/Payment/ReversalRequest", sent.Url);
        using var json = JsonDocument.Parse(sent.Body);
        Assert.Equal(123, json.RootElement.GetProperty("Token").GetInt64());
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
    }

    [Theory]
    [InlineData(-126)]
    [InlineData(-1528)]
    public async Task Reversal_refusal_does_not_claim_failed_payment(int status)
    {
        using var bank = new BankFixture((_, _) => Reply($$"""{"Status":{{status}},"Token":123,"Message":"refused"}"""));
        var result = await bank.Provider.ReverseAsync(Context(), TestContext.Current.CancellationToken);
        Assert.Equal($"PARSIAN_STATUS_{status}", result.Error!.Code);
        Assert.Single(bank.Handler.Sent);
    }

    [Fact]
    public async Task Unknown_reversal_status_is_retained_without_final_failure()
    {
        using var bank = new BankFixture((_, _) => Reply("""{"Status":-999,"Token":123}"""));
        var result = await bank.Provider.ReverseAsync(Context(), TestContext.Current.CancellationToken);
        Assert.Equal(GatewayReversalState.Unknown, result.Value!.State);
        Assert.Equal("-999", result.Value.ProviderCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Token\":123}")]
    [InlineData("{\"Status\":0,\"Token\":999}")]
    public async Task Malformed_reversal_response_is_rejected(string json)
    {
        using var bank = new BankFixture((_, _) => Reply(json));
        Assert.Equal(PaymentGatewayErrors.InvalidResponse,
            (await bank.Provider.ReverseAsync(Context(), TestContext.Current.CancellationToken)).Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reversal_transport_retry_uses_a_fresh_request(bool exhaust)
    {
        using var bank = new BankFixture((attempt, _) =>
            attempt == 1 || exhaust ? throw new HttpRequestException("connection lost") : Reply(Success));
        var result = await bank.Provider.ReverseAsync(Context(), TestContext.Current.CancellationToken);
        Assert.Equal(2, bank.Handler.Sent.Count);
        Assert.NotSame(bank.Handler.Requests[0], bank.Handler.Requests[1]);
        if (exhaust) Assert.Equal(PaymentGatewayErrors.Unavailable, result.Error);
        else Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Reversal_http_error_is_not_interpreted_as_a_bank_status()
    {
        using var bank = new BankFixture((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(Success) });
        var result = await bank.Provider.ReverseAsync(Context(), TestContext.Current.CancellationToken);
        Assert.Equal("PARSIAN_HTTP_500", result.Error!.Code);
        Assert.Single(bank.Handler.Sent);
    }

    [Fact]
    public async Task Sale_transport_failure_is_not_automatically_retried()
    {
        using var bank = new BankFixture((_, _) => throw new HttpRequestException("lost"));
        Assert.Equal(PaymentGatewayErrors.Unavailable,
            (await bank.Provider.CreateLinkAsync(LinkRequest(), TestContext.Current.CancellationToken)).Error);
        Assert.Single(bank.Handler.Sent);
    }

    [Fact]
    public async Task Cancellation_before_callback_or_sale_prevents_work()
    {
        using var bank = new BankFixture((_, _) => Reply(Success));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bank.Provider.CreateLinkAsync(LinkRequest(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bank.Provider.VerifyCallbackAsync(new(Context(), PaidFields()), cancellation.Token));
        Assert.Empty(bank.Handler.Sent);
    }

    [Fact]
    public async Task Maximum_integer_rial_value_is_not_rounded_or_overflowed()
    {
        using var bank = new BankFixture((_, _) => Reply(Success));
        var result = await bank.Provider.CreateLinkAsync(LinkRequest() with { Amount = Money.Create(long.MaxValue) }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(Assert.Single(bank.Handler.Sent).Body);
        Assert.Equal(long.MaxValue, json.RootElement.GetProperty("Amount").GetInt64());
    }

    private static Dictionary<string, string> PaidFields(string rrn = "456") => new() { ["status"] = "0", ["RRN"] = rrn };

    internal const string Success = """{"Status":0,"Token":123,"RRN":456,"CardNumberMasked":"masked","Message":"ok"}""";
    internal static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    internal static GatewayLinkRequest LinkRequest() => new(1001, 42, Money.Create(1000), new Uri("https://merchant.test/callback"));
    internal static GatewayPaymentContext Context(string token = "123", string? reference = null, string provider = "parsian") =>
        new(42, Money.Create(1000), GatewayAttempt.Create(provider, 1001, token, RequestedAt, reference));

    internal sealed class BankFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public RecordingHandler Handler { get; }
        public IPaymentGatewayProvider Provider { get; }

        public BankFixture(Func<int, CancellationToken, HttpResponseMessage> respond, Dictionary<string, string?>? overrides = null)
        {
            Handler = new(respond);
            var config = new Dictionary<string, string?>
            {
                ["Payment:Gateways:Parsian:Enabled"] = "true",
                ["Payment:Gateways:Parsian:LoginAccount"] = "test-only-credential"
            };
            if (overrides is not null) foreach (var entry in overrides) config[entry.Key] = entry.Value;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPaymentModule(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
            services.AddHttpClient("Payment.Parsian").ConfigurePrimaryHttpMessageHandler(() => Handler);
            _services = services.BuildServiceProvider();
            var resolved = _services.GetRequiredService<IPaymentGatewayProviderSelector>().SelectRetained("parsian");
            Assert.True(resolved.IsSuccess, resolved.Error?.Code);
            Provider = resolved.Value!;
        }

        public void Dispose() { _services.Dispose(); Handler.Dispose(); }
    }

    internal sealed class RecordingHandler(Func<int, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Sent { get; } = [];
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Sent.Add(new(request.RequestUri!.AbsoluteUri, request.Method.Method,
                request.Content?.Headers.ContentType?.MediaType,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(Sent.Count, cancellationToken);
        }
    }

    internal sealed record SentRequest(string Url, string Method, string? ContentType, string Body);
}
