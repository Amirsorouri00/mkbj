using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Payment.Application.Ports.Outbound;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EwaysVNext.Modules.Payment.GatewayTests;

public sealed class HttpIsolationTests
{
    [Fact]
    public async Task Provider_registration_leaves_an_existing_http_client_unchanged()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("Existing.Client", client =>
        {
            client.BaseAddress = new Uri("https://existing.test/api/");
            client.DefaultRequestHeaders.Add("X-Existing", "original");
            client.Timeout = TimeSpan.FromSeconds(37);
        });
        services.AddPaymentModule(Configuration());
        using var handler = new ParsianTests.RecordingHandler((_, _) => ParsianTests.Reply(ParsianTests.Success));
        services.AddHttpClient("Payment.Parsian").ConfigurePrimaryHttpMessageHandler(() => handler);
        using var sp = services.BuildServiceProvider();
        using var existing = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Existing.Client");
        var provider = sp.GetRequiredService<IPaymentGatewayProviderSelector>().SelectRetained("parsian");
        Assert.True(provider.IsSuccess);
        await provider.Value!.CreateLinkAsync(ParsianTests.LinkRequest(), TestContext.Current.CancellationToken);
        Assert.Equal("https://existing.test/api/", existing.BaseAddress!.AbsoluteUri);
        Assert.Equal("original", Assert.Single(existing.DefaultRequestHeaders.GetValues("X-Existing")));
        Assert.Equal(TimeSpan.FromSeconds(37), existing.Timeout);
        Assert.False(handler.Requests[0].Headers.Contains("X-Existing"));
    }

    [Theory]
    [InlineData("https://custom.test/services", "https://custom.test/services/Payment/SalePaymentRequest")]
    [InlineData("https://custom.test/services/", "https://custom.test/services/Payment/SalePaymentRequest")]
    public async Task Provider_local_base_address_preserves_path_with_or_without_trailing_slash(string baseUrl, string expected)
    {
        using var bank = new ParsianTests.BankFixture((_, _) => ParsianTests.Reply(ParsianTests.Success),
            new() { ["Payment:Gateways:Parsian:BaseUrl"] = baseUrl });
        await bank.Provider.CreateLinkAsync(ParsianTests.LinkRequest(), TestContext.Current.CancellationToken);
        Assert.Equal(expected, Assert.Single(bank.Handler.Sent).Url);
    }

    [Theory]
    [InlineData("LoginAccount", "")]
    [InlineData("BaseUrl", "http://unsafe.test/api")]
    [InlineData("PaymentPageUrl", "http://unsafe.test/pay")]
    [InlineData("BaseUrl", "https://bank.test/api?unexpected=query")]
    [InlineData("RequestTimeout", "00:00:00")]
    public void Invalid_enabled_provider_configuration_fails_before_any_bank_call(string key, string value)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentModule(Configuration(new() { ["Payment:Gateways:Parsian:" + key] = value }));
        using var sp = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void Bank_wire_models_are_not_exported_or_referenced_by_application()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentModule(Configuration());
        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IPaymentGatewayProviderSelector>().SelectRetained("parsian").Value;
        Assert.NotNull(provider);
        Assert.DoesNotContain(provider.GetType().Assembly.GetExportedTypes(), type =>
            type.Namespace?.EndsWith(".Parsian.Contracts", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(typeof(IPaymentGateway).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name!.Contains("Adapter", StringComparison.Ordinal));
    }

    private static IConfiguration Configuration(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Payment:Gateways:Parsian:Enabled"] = "true",
            ["Payment:Gateways:Parsian:LoginAccount"] = "test-only-credential"
        };
        if (overrides is not null) foreach (var item in overrides) values[item.Key] = item.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
