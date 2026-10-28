using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Mellat;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EwaysVNext.Modules.Payment.GatewayTests;

public sealed class MellatInquiryTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 29, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Request_uses_the_BOS_contract_and_returns_the_bank_amount()
    {
        using var bank = new BankFixture((_, _) => Reply(Success(amount: 130000)));

        var result = await bank.Inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(Money.Create(130000), result.Value!.BankAmount);
        Assert.Equal("456", result.Value.ProviderReference);
        Assert.Equal(new DateTimeOffset(2022, 12, 6, 16, 1, 29, TimeSpan.FromHours(3.5)),
            result.Value.TransactionTime);
        var sent = Assert.Single(bank.Handler.Sent);
        Assert.Equal("https://bos.bpm.bankmellat.ir/bhrws/transactionInfo/getTransactionById", sent.Url);
        Assert.Equal("Basic", sent.Authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("bos-user:bos-password")), sent.Authorization?.Parameter);
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal(456, body.RootElement.GetProperty("referenceId").GetInt64());
        Assert.Single(body.RootElement.EnumerateObject());
        Assert.DoesNotContain("Archive", sent.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-numeric")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Invalid_reference_is_refused_before_HTTP(string? reference)
    {
        using var bank = new BankFixture((_, _) => Reply(Success()));

        var result = await bank.Inquiry.InquiryAsync(Context(reference), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentGatewayErrors.InvalidContext, result.Error);
        Assert.Empty(bank.Handler.Sent);
    }

    [Fact]
    public async Task Amount_mismatch_remains_a_provider_observation()
    {
        using var bank = new BankFixture((_, _) => Reply(Success(amount: 999)));

        var result = await bank.Inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(999, result.Value!.BankAmount.Amount);
    }

    [Fact]
    public async Task Transaction_not_found_has_the_shared_not_found_semantics()
    {
        using var bank = new BankFixture((_, _) => Reply("""{"success":false,"responseCode":"914","responseMessage":"not found","transactionInfoList":[]}"""));

        var result = await bank.Inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentGatewayErrors.TransactionNotFound, result.Error);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("558")]
    [InlineData("550")]
    [InlineData("555")]
    public async Task Documented_refusals_keep_Mellat_owned_codes(string code)
    {
        using var bank = new BankFixture((_, _) => Reply($$"""{"success":false,"responseCode":"{{code}}","responseMessage":"refused","transactionInfoList":[]}"""));

        var result = await bank.Inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Equal("MELLAT_INQUIRY_STATUS_" + code, result.Error!.Code);
    }

    [Theory]
    [InlineData("""{"success":true,"responseCode":"000","transactionInfoList":[]}""")]
    [InlineData("""{"success":true,"responseCode":"000","transactionInfoList":[{"terminalId":1234,"internalReferenceId":456,"amount":100},{"terminalId":1234,"internalReferenceId":456,"amount":100}]}""")]
    [InlineData("""{"success":true,"responseCode":"000","transactionInfoList":[{"terminalId":1234,"internalReferenceId":999,"amount":100}]}""")]
    [InlineData("""{"success":true,"responseCode":"000","transactionInfoList":[{"terminalId":9999,"internalReferenceId":456,"amount":100}]}""")]
    [InlineData("""{"success":true,"responseCode":"000","transactionInfoList":[{"terminalId":1234,"internalReferenceId":456,"amount":0}]}""")]
    [InlineData("{}")]
    [InlineData("""{"success":false,"responseCode":"000","transactionInfoList":[]}""")]
    [InlineData("not-json")]
    public async Task Malformed_or_uncorrelated_success_is_rejected(string json)
    {
        using var bank = new BankFixture((_, _) => Reply(json));

        var result = await bank.Inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentGatewayErrors.InvalidResponse, result.Error);
    }

    [Fact]
    public async Task Read_only_transport_retry_uses_a_fresh_request()
    {
        using var bank = new BankFixture((attempt, _) => attempt == 1
            ? throw new HttpRequestException("transient")
            : Reply(Success()));

        var result = await bank.Inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(2, bank.Handler.Attempts);
        Assert.Equal(2, bank.Handler.Sent.Count);
    }

    [Fact]
    public async Task Disabled_inquiry_fails_before_HTTP_without_disabling_Mellat()
    {
        using var bank = new BankFixture((_, _) => Reply(Success()), new()
        {
            ["Payment:Gateways:Mellat:Inquiry:Enabled"] = "false",
            ["Payment:Gateways:Mellat:Inquiry:BaseUrl"] = "not-a-url",
            ["Payment:Gateways:Mellat:Inquiry:RequestTimeout"] = "00:00:00"
        });

        var result = await bank.Inquiry.InquiryAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Equal("MELLAT_INQUIRY_NOT_CONFIGURED", result.Error!.Code);
        Assert.Empty(bank.Handler.Sent);
        Assert.Equal("mellat", bank.Provider.Name);
    }

    [Theory]
    [InlineData("BaseUrl", "http://unsafe.test")]
    [InlineData("UserName", "")]
    [InlineData("Password", "")]
    [InlineData("RequestTimeout", "00:00:00")]
    public void Invalid_enabled_inquiry_configuration_fails_startup_validation(string key, string value)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentModule(Configuration(new()
        {
            ["Payment:Gateways:Mellat:Inquiry:" + key] = value
        }));
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    private static string Success(decimal amount = 1000) => $$"""
        {"success":true,"responseCode":"000","responseMessage":"approved","transactionInfoList":[{"terminalId":1234,"internalReferenceId":456,"transactionDate":"2022/12/06","transactionTime":"16:01:29","amount":{{amount}}}]}
        """;

    private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static GatewayPaymentContext Context(string? reference = "456") =>
        new(42, Money.Create(1000), GatewayAttempt.Create("mellat", 1001, "token", RequestedAt, reference));

    private static IConfiguration Configuration(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Payment:Gateways:Mellat:Enabled"] = "true",
            ["Payment:Gateways:Mellat:TerminalId"] = "1234",
            ["Payment:Gateways:Mellat:UserName"] = "merchant-user",
            ["Payment:Gateways:Mellat:UserPassword"] = "merchant-password",
            ["Payment:Gateways:Mellat:Inquiry:Enabled"] = "true",
            ["Payment:Gateways:Mellat:Inquiry:UserName"] = "bos-user",
            ["Payment:Gateways:Mellat:Inquiry:Password"] = "bos-password"
        };
        if (overrides is not null)
            foreach (var item in overrides) values[item.Key] = item.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class BankFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public RecordingHandler Handler { get; }
        public IPaymentGatewayProvider Provider { get; }
        public IPaymentInquiryProvider Inquiry => (IPaymentInquiryProvider)Provider;

        public BankFixture(Func<int, CancellationToken, HttpResponseMessage> response,
            Dictionary<string, string?>? overrides = null)
        {
            Handler = new(response);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPaymentModule(Configuration(overrides));
            services.AddHttpClient(MellatRegistration.InquiryHttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Handler);
            _services = services.BuildServiceProvider();
            Provider = _services.GetRequiredService<IPaymentGatewayProviderSelector>()
                .SelectRetained("mellat").Value!;
        }

        public void Dispose()
        {
            _services.Dispose();
            Handler.Dispose();
        }
    }

    private sealed class RecordingHandler(Func<int, CancellationToken, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Attempts { get; private set; }
        public List<SentRequest> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            Sent.Add(new(request.RequestUri!.AbsoluteUri,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization));
            return response(Attempts, cancellationToken);
        }
    }

    private sealed record SentRequest(string Url, string Body, AuthenticationHeaderValue? Authorization);
}
