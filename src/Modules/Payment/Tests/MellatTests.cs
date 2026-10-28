using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.GatewayTests;

public sealed class MellatTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 29, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sale_uses_required_contract_fields_and_returns_post_handoff()
    {
        using var bank = new BankFixture((_, _) => Reply(SaleSuccess));

        var result = await bank.Provider.CreateLinkAsync(LinkRequest(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal("CaseSensitiveRef", result.Value!.Token);
        Assert.Equal("https://bpm.shaparak.ir/pgwchannel/startpay.mellat", result.Value.Redirect.Url.AbsoluteUri);
        Assert.Equal(GatewayRedirectMethod.Post, result.Value.Redirect.Method);
        Assert.Equal("CaseSensitiveRef", Assert.Single(result.Value.Redirect.Fields).Value);

        var sent = Assert.Single(bank.Handler.Sent);
        Assert.Equal("https://bpm.shaparak.ir/pgwchannel/rest/bpPayRequest", sent.Url);
        Assert.Equal("Basic", sent.Authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("merchant-user:merchant-password")), sent.Authorization?.Parameter);
        using var json = JsonDocument.Parse(sent.Body);
        Assert.Equal(1234, json.RootElement.GetProperty("terminalId").GetInt64());
        Assert.Equal("merchant-user", json.RootElement.GetProperty("userName").GetString());
        Assert.Equal("merchant-password", json.RootElement.GetProperty("userPassword").GetString());
        Assert.Equal(1001, json.RootElement.GetProperty("orderId").GetInt64());
        Assert.Equal("1000", json.RootElement.GetProperty("amount").GetString());
        Assert.Equal("20260321", json.RootElement.GetProperty("localDate").GetString());
        Assert.Equal("153045", json.RootElement.GetProperty("localTime").GetString());
        Assert.Equal("", json.RootElement.GetProperty("additionalData").GetString());
        Assert.Equal("https://merchant.test/callback", json.RootElement.GetProperty("callBackUrl").GetString());
        Assert.Equal("0", json.RootElement.GetProperty("payerId").GetString());
        Assert.False(json.RootElement.TryGetProperty("mobileNo", out _));
        Assert.False(json.RootElement.TryGetProperty("encPan", out _));
        Assert.False(json.RootElement.TryGetProperty("panHiddenMode", out _));
        Assert.False(json.RootElement.TryGetProperty("cartItem", out _));
        Assert.False(json.RootElement.TryGetProperty("enc", out _));
    }

    [Theory]
    [InlineData("0", GatewayPaymentState.Confirmed)]
    [InlineData("43", GatewayPaymentState.Confirmed)]
    [InlineData("45", GatewayPaymentState.Confirmed)]
    [InlineData("48", GatewayPaymentState.Reversed)]
    public async Task Paid_callback_is_verified_with_request_id_for_both_mellat_ids(string code, GatewayPaymentState state)
    {
        using var bank = new BankFixture((_, _) => Reply(Operation(code)));

        var result = await bank.Provider.VerifyCallbackAsync(new(Context(reference: null), PaidFields()), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(state, result.Value!.State);
        Assert.Equal(code, result.Value.ProviderCode);
        Assert.Equal("456", result.Value.ProviderReference);
        var sent = Assert.Single(bank.Handler.Sent);
        Assert.Equal("https://bpm.shaparak.ir/pgwchannel/rest/bpVerifyRequest", sent.Url);
        using var json = JsonDocument.Parse(sent.Body);
        Assert.Equal(1001, json.RootElement.GetProperty("orderId").GetInt64());
        Assert.Equal(1001, json.RootElement.GetProperty("saleOrderId").GetInt64());
        Assert.Equal(456, json.RootElement.GetProperty("saleReferenceId").GetInt64());
    }

    [Theory]
    [InlineData("0", GatewayReversalState.Reversed)]
    [InlineData("48", GatewayReversalState.Reversed)]
    [InlineData("43", GatewayReversalState.StillConfirmed)]
    [InlineData("45", GatewayReversalState.StillConfirmed)]
    public async Task Reverse_uses_request_id_for_both_mellat_ids(string code, GatewayReversalState state)
    {
        using var bank = new BankFixture((_, _) => Reply(Operation(code)));

        var result = await bank.Provider.ReverseAsync(Context(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(state, result.Value!.State);
        var sent = Assert.Single(bank.Handler.Sent);
        Assert.Equal("https://bpm.shaparak.ir/pgwchannel/rest/bpReversalRequest", sent.Url);
        using var json = JsonDocument.Parse(sent.Body);
        Assert.Equal(1001, json.RootElement.GetProperty("saleOrderId").GetInt64());
        Assert.Equal(456, json.RootElement.GetProperty("saleReferenceId").GetInt64());
    }

    [Fact]
    public async Task Callback_requires_exact_ref_id_and_matching_sale_order_id_before_verify()
    {
        using var bank = new BankFixture((_, _) => Reply(Operation("0")));
        var fields = PaidFields();

        fields["RefId"] = "casesensitiveref";
        Assert.Equal(PaymentGatewayErrors.InvalidCallback,
            (await bank.Provider.VerifyCallbackAsync(new(Context(), fields), TestContext.Current.CancellationToken)).Error);
        fields["RefId"] = "CaseSensitiveRef";
        fields["SaleOrderId"] = "1002";
        Assert.Equal(PaymentGatewayErrors.InvalidCallback,
            (await bank.Provider.VerifyCallbackAsync(new(Context(), fields), TestContext.Current.CancellationToken)).Error);
        Assert.Empty(bank.Handler.Sent);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"encRefId\":\"CaseSensitiveRef\",\"internalRefrenceId\":456}")]
    [InlineData("{\"resCode\":\"0\",\"encRefId\":null,\"internalRefrenceId\":456}")]
    [InlineData("{\"resCode\":\"0\",\"encRefId\":\"CaseSensitiveRef\",\"internalRefrenceId\":0}")]
    [InlineData("{\"ResCode\":\"0\",\"encRefId\":\"CaseSensitiveRef\",\"internalRefrenceId\":456}")]
    [InlineData("null")]
    [InlineData("not-json")]
    public async Task Malformed_sale_response_is_rejected(string json)
    {
        using var bank = new BankFixture((_, _) => Reply(json));
        Assert.Equal(PaymentGatewayErrors.InvalidResponse,
            (await bank.Provider.CreateLinkAsync(LinkRequest(), TestContext.Current.CancellationToken)).Error);
    }

    [Fact]
    public async Task Sale_refusal_preserves_the_mellat_code()
    {
        using var bank = new BankFixture((_, _) => Reply("""{"resCode":"25","encRefId":null,"internalRefrenceId":0}"""));
        var result = await bank.Provider.CreateLinkAsync(LinkRequest(), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal("MELLAT_STATUS_25", result.Error!.Code);
    }

    [Theory]
    [InlineData("17", GatewayPaymentState.Failed)]
    [InlineData("12", GatewayPaymentState.Failed)]
    [InlineData("34", GatewayPaymentState.Unknown)]
    [InlineData("9999", GatewayPaymentState.Unknown)]
    public async Task Unpaid_callback_is_normalized_without_verifying(string code, GatewayPaymentState state)
    {
        using var bank = new BankFixture((_, _) => Reply(Operation("0")));
        var result = await bank.Provider.VerifyCallbackAsync(new(Context(), new Dictionary<string, string>
        {
            ["RefId"] = "CaseSensitiveRef", ["ResCode"] = code, ["SaleOrderId"] = "1001"
        }), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(state, result.Value!.State);
        Assert.Equal(code, result.Value.ProviderCode);
        Assert.Null(result.Value.ProviderReference);
        Assert.Empty(bank.Handler.Sent);
    }

    [Theory]
    [InlineData("refid", "CaseSensitiveRef", "ResCode", "0", "SaleOrderId", "1001")]
    [InlineData("RefId", "CaseSensitiveRef", "resCode", "0", "SaleOrderId", "1001")]
    [InlineData("RefId", "CaseSensitiveRef", "ResCode", "0", "saleOrderId", "1001")]
    [InlineData("RefId", "CaseSensitiveRef", "ResCode", "0", "SaleOrderId", "invalid")]
    public async Task Callback_field_names_and_values_are_strict(
        string refKey, string refValue, string codeKey, string codeValue, string orderKey, string orderValue)
    {
        using var bank = new BankFixture((_, _) => Reply(Operation("0")));
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [refKey] = refValue, [codeKey] = codeValue, [orderKey] = orderValue, ["SaleReferenceId"] = "456"
        };
        Assert.Equal(PaymentGatewayErrors.InvalidCallback,
            (await bank.Provider.VerifyCallbackAsync(new(Context(), fields), TestContext.Current.CancellationToken)).Error);
        Assert.Empty(bank.Handler.Sent);
    }

    [Fact]
    public async Task Final_amount_is_not_read()
    {
        using var bank = new BankFixture((_, _) => Reply(Operation("0")));
        var fields = PaidFields();
        fields["FinalAmount"] = "not-an-amount";
        var result = await bank.Provider.VerifyCallbackAsync(new(Context(), fields), TestContext.Current.CancellationToken);
        Assert.Equal(GatewayPaymentState.Confirmed, result.Value!.State);
    }

    [Theory]
    [InlineData("30", GatewayPaymentState.Unknown)]
    [InlineData("9999", GatewayPaymentState.Unknown)]
    [InlineData("42", GatewayPaymentState.Failed)]
    [InlineData("44", GatewayPaymentState.Failed)]
    public async Task Verify_distinguishes_unknown_outcome_from_refusal(string code, GatewayPaymentState state)
    {
        using var bank = new BankFixture((_, _) => Reply(Operation(code)));
        var result = await bank.Provider.VerifyCallbackAsync(new(Context(), PaidFields()), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(state, result.Value!.State);
        Assert.Equal(code, result.Value.ProviderCode);
    }

    [Theory]
    [InlineData("30", true)]
    [InlineData("9999", true)]
    [InlineData("42", false)]
    public async Task Reverse_distinguishes_unknown_outcome_from_refusal(string code, bool unknown)
    {
        using var bank = new BankFixture((_, _) => Reply(Operation(code)));
        var result = await bank.Provider.ReverseAsync(Context(), TestContext.Current.CancellationToken);
        if (unknown) Assert.Equal(GatewayReversalState.Unknown, result.Value!.State);
        else Assert.Equal("MELLAT_STATUS_42", result.Error!.Code);
    }

    [Fact]
    public async Task Reverse_requires_the_confirmed_sale_reference()
    {
        using var bank = new BankFixture((_, _) => Reply("{}"));
        var result = await bank.Provider.ReverseAsync(Context(reference: null), TestContext.Current.CancellationToken);
        Assert.Equal(PaymentGatewayErrors.InvalidContext, result.Error);
        Assert.Empty(bank.Handler.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verify_transport_failure_retries_with_a_fresh_request_then_is_unknown(bool exhaust)
    {
        using var bank = new BankFixture((attempt, _) => attempt == 1 || exhaust
            ? throw new HttpRequestException("connection lost")
            : Reply(Operation("0")));
        var result = await bank.Provider.VerifyCallbackAsync(new(Context(), PaidFields()), TestContext.Current.CancellationToken);
        Assert.Equal(exhaust ? GatewayPaymentState.Unknown : GatewayPaymentState.Confirmed, result.Value!.State);
        Assert.Equal(2, bank.Handler.Sent.Count);
        Assert.NotSame(bank.Handler.Requests[0], bank.Handler.Requests[1]);
        Assert.Equal(bank.Handler.Sent[0].Body, bank.Handler.Sent[1].Body);
    }

    [Fact]
    public async Task Reverse_transport_failure_retries_with_a_fresh_request()
    {
        using var bank = new BankFixture((attempt, _) => attempt == 1
            ? throw new HttpRequestException("connection lost")
            : Reply(Operation("0")));
        var result = await bank.Provider.ReverseAsync(Context(), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(2, bank.Handler.Sent.Count);
    }

    [Fact]
    public async Task Inquiry_is_offered_but_safe_when_not_configured()
    {
        using var bank = new BankFixture((_, _) => Reply("{}"));
        var inquiry = Assert.IsAssignableFrom<IPaymentInquiryProvider>(bank.Provider);
        var result = await inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);
        Assert.Equal("MELLAT_INQUIRY_NOT_CONFIGURED", result.Error!.Code);
        Assert.Empty(bank.Handler.Sent);
    }

    [Fact]
    public async Task Another_providers_context_is_refused_without_http()
    {
        using var bank = new BankFixture((_, _) => Reply(Operation("0")));
        var parsian = new GatewayPaymentContext(42, Money.Create(1000), GatewayAttempt.Create("parsian", 1001, "CaseSensitiveRef", RequestedAt, "456"));
        Assert.Equal(PaymentGatewayErrors.InvalidContext,
            (await bank.Provider.VerifyCallbackAsync(new(parsian, PaidFields()), TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PaymentGatewayErrors.InvalidContext,
            (await bank.Provider.ReverseAsync(parsian, TestContext.Current.CancellationToken)).Error);
        Assert.Empty(bank.Handler.Sent);
    }

    private static string Operation(string code) => $$"""{"resCode":"{{code}}","encRefId":null,"internalRefrenceId":456}""";

    private static Dictionary<string, string> PaidFields() => new()
    {
        ["RefId"] = "CaseSensitiveRef", ["ResCode"] = "0", ["SaleOrderId"] = "1001", ["SaleReferenceId"] = "456"
    };

    [Theory]
    [InlineData("999")]
    [InlineData("1000.50")]
    [InlineData("9223372036854775808")]
    public async Task Invalid_bank_amount_is_refused_without_http(string amount)
    {
        using var bank = new BankFixture((_, _) => Reply("{}"));
        var request = LinkRequest() with
        {
            Amount = Money.Create(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture))
        };
        Assert.Equal(PaymentGatewayErrors.UnsupportedAmount,
            (await bank.Provider.CreateLinkAsync(request, TestContext.Current.CancellationToken)).Error);
        Assert.Empty(bank.Handler.Sent);
    }

    [Theory]
    [InlineData("TerminalId", "0")]
    [InlineData("UserName", "")]
    [InlineData("UserPassword", "")]
    [InlineData("BaseUrl", "http://unsafe.test")]
    [InlineData("PaymentPageUrl", "https://bank.test/pay?query=unsafe")]
    [InlineData("RequestTimeout", "00:00:00")]
    public void Invalid_enabled_configuration_fails_at_startup(string key, string value)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentModule(Configuration(new() { ["Payment:Gateways:Mellat:" + key] = value }));
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Provider_is_registered_only_when_enabled(bool enabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentModule(Configuration(new() { ["Payment:Gateways:Mellat:Enabled"] = enabled.ToString() }));
        using var provider = services.BuildServiceProvider();
        Assert.Equal(enabled, provider.GetRequiredService<IPaymentGatewayProviderSelector>().SelectRetained("mellat").IsSuccess);
    }

    [Fact]
    public async Task Provider_registration_does_not_change_an_existing_http_client()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("Existing.Client", client =>
        {
            client.BaseAddress = new Uri("https://existing.test/api/");
            client.DefaultRequestHeaders.Add("X-Existing", "original");
        });
        services.AddPaymentModule(Configuration());
        using var handler = new RecordingHandler((_, _) => Reply(SaleSuccess));
        services.AddHttpClient("Payment.Mellat").ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        using var existing = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Existing.Client");

        await provider.GetRequiredService<IPaymentGatewayProviderSelector>().SelectRetained("mellat").Value!
            .CreateLinkAsync(LinkRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("https://existing.test/api/", existing.BaseAddress!.AbsoluteUri);
        Assert.Equal("original", Assert.Single(existing.DefaultRequestHeaders.GetValues("X-Existing")));
        Assert.False(handler.Requests[0].Headers.Contains("X-Existing"));
    }

    private const string SaleSuccess = """{"resCode":"0","encRefId":"CaseSensitiveRef","internalRefrenceId":456}""";
    private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static GatewayLinkRequest LinkRequest() => new(1001, 42, Money.Create(1000), new Uri("https://merchant.test/callback"));
    private static GatewayPaymentContext Context(string? reference = "456") =>
        new(42, Money.Create(1000), GatewayAttempt.Create("mellat", 1001, "CaseSensitiveRef", RequestedAt, reference));

    private static IConfiguration Configuration(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Payment:Gateways:Mellat:Enabled"] = "true",
            ["Payment:Gateways:Mellat:TerminalId"] = "1234",
            ["Payment:Gateways:Mellat:UserName"] = "merchant-user",
            ["Payment:Gateways:Mellat:UserPassword"] = "merchant-password"
        };
        if (overrides is not null) foreach (var item in overrides) values[item.Key] = item.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class BankFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public RecordingHandler Handler { get; }
        public IPaymentGatewayProvider Provider { get; }

        public BankFixture(Func<int, CancellationToken, HttpResponseMessage> response)
        {
            Handler = new(response);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPaymentModule(Configuration());
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2026, 3, 21, 12, 0, 45, TimeSpan.Zero)));
            services.AddHttpClient("Payment.Mellat").ConfigurePrimaryHttpMessageHandler(() => Handler);
            _services = services.BuildServiceProvider();
            Provider = _services.GetRequiredService<IPaymentGatewayProviderSelector>().SelectRetained("mellat").Value!;
        }

        public void Dispose() { _services.Dispose(); Handler.Dispose(); }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingHandler(Func<int, CancellationToken, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<SentRequest> Sent { get; } = [];
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Sent.Add(new(request.RequestUri!.AbsoluteUri,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization));
            return response(Sent.Count, cancellationToken);
        }
    }

    private sealed record SentRequest(string Url, string Body, AuthenticationHeaderValue? Authorization);
}
