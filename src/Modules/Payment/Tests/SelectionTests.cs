using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Payment.Application.Domain;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using EwaysVNext.Modules.Payment.Application.Ports.Inbound;
using EwaysVNext.Modules.Payment.Application.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EwaysVNext.Outcomes;
using Xunit;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

namespace EwaysVNext.Modules.Payment.GatewayTests;

public sealed class SelectionTests
{
    [Fact]
    public void Initiation_accepts_a_runtime_provider_identifier()
    {
        Assert.Contains(typeof(IPaymentGatewayService).GetMethods(), m =>
            m.Name == "CreateLinkAsync" && m.GetParameters().Any(p => p.Name == "providerName" && p.ParameterType == typeof(string)));
    }

    [Fact]
    public async Task Existing_default_literal_call_keeps_policy_based_selection()
    {
        using var services = Services(new ProbeProvider("alpha"));
        var service = services.GetRequiredService<IPaymentGatewayService>();
        var result = await service.CreateLinkAsync(new Transaction(), new Uri("https://merchant.test/callback"), default);
        Assert.Equal("alpha", result.Value!.Attempt.ProviderName);
    }

    [Fact]
    public void Registration_resolves_case_insensitively_and_does_not_overwrite_duplicates()
    {
        using var services = Services(new ProbeProvider("alpha"), new ProbeProvider("ALPHA"), new ProbeProvider("beta"));
        var selector = services.GetRequiredService<IPaymentGatewayProviderSelector>();
        Assert.Equal("PAYMENT_PROVIDER_AMBIGUOUS", selector.SelectRetained("alpha").Error!.Code);
        Assert.Equal("beta", selector.SelectRetained("BETA").Value!.Name);
        Assert.Equal("PAYMENT_PROVIDER_NOT_REGISTERED", selector.SelectRetained("missing").Error!.Code);
    }

    [Fact]
    public void Link_creation_plan_follows_configured_priority_and_carries_the_timeout()
    {
        using var services = Services(new ProbeProvider("beta"), new ProbeProvider("alpha"));
        var plan = services.GetRequiredService<IPaymentGatewayProviderSelector>().SelectForLinkCreation(null);
        Assert.Equal(["alpha", "beta"], plan.Value!.Providers.Select(p => p.Name));
        Assert.Equal(TimeSpan.FromSeconds(10), plan.Value.Timeout);
    }

    [Fact]
    public void Explicit_name_plans_only_that_provider()
    {
        using var services = Services(new ProbeProvider("alpha"), new ProbeProvider("beta"));
        var plan = services.GetRequiredService<IPaymentGatewayProviderSelector>().SelectForLinkCreation("BETA");
        Assert.Equal("beta", Assert.Single(plan.Value!.Providers).Name);
    }

    [Fact]
    public void Unregistered_configured_names_are_skipped_and_none_left_is_exhausted()
    {
        using var onlyFallback = Services(new ProbeProvider("beta"));
        Assert.Equal("beta", Assert.Single(onlyFallback.GetRequiredService<IPaymentGatewayProviderSelector>()
            .SelectForLinkCreation(null).Value!.Providers).Name);

        using var none = Services(new ProbeProvider("unlisted"));
        Assert.Equal("PAYMENT_GATEWAYS_EXHAUSTED", none.GetRequiredService<IPaymentGatewayProviderSelector>()
            .SelectForLinkCreation(null).Error!.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_selection_calls_only_the_requested_provider(bool reject)
    {
        var alpha = new ProbeProvider("alpha");
        var beta = new ProbeProvider("beta") { Reject = reject };
        using var services = Services(alpha, beta);
        var result = await Select(services, "BETA");
        Assert.Equal(0, alpha.Calls);
        Assert.Equal(1, beta.Calls);
        if (reject) Assert.Equal(PaymentGatewayErrors.Unavailable, result.Error);
        else Assert.Equal("beta", result.Value!.Attempt.ProviderName);
    }

    [Theory]
    [InlineData("missing", "PAYMENT_PROVIDER_NOT_REGISTERED")]
    [InlineData("", "PAYMENT_PROVIDER_NAME_EMPTY")]
    [InlineData("  ", "PAYMENT_PROVIDER_NAME_EMPTY")]
    public async Task Invalid_explicit_selection_does_not_use_the_default(string name, string code)
    {
        var alpha = new ProbeProvider("alpha");
        using var services = Services(alpha);
        Assert.Equal(code, (await Select(services, name)).Error!.Code);
        Assert.Equal(0, alpha.Calls);
    }

    [Fact]
    public void Module_composition_resolves_the_service_under_strict_container_validation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentModule(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:Gateways:DefaultProvider"] = "alpha"
        }).Build());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.IsType<PaymentGatewayService>(scope.ServiceProvider.GetRequiredService<IPaymentGatewayService>());
    }

    private static Task<Result<PaymentLinkResult>> Select(ServiceProvider services, string name)
    {
        return services.GetRequiredService<IPaymentGatewayService>().CreateLinkAsync(
            new Transaction(), new Uri("https://merchant.test/callback"), name, TestContext.Current.CancellationToken);
    }

    internal static ServiceProvider Services(params ProbeProvider[] providers)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IIdGenerator, TestIds>();
        foreach (var provider in providers) services.AddSingleton<IPaymentGatewayProvider>(provider);
        services.AddPaymentModule(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:Gateways:DefaultProvider"] = "alpha",
            ["Payment:Gateways:FallbackProviders:0"] = "beta"
        }).Build());
        return services.BuildServiceProvider();
    }

    internal sealed class Transaction : AggregateRoot<long>, IBankTransaction
    {
        long IBankTransaction.Id => 42;
        public Money Amount { get; } = Money.Create(1000);
        public GatewayAttempt? GatewayAttempt => null;
    }

    private sealed class TestIds : IIdGenerator
    {
        public long Generate() => 1001;
    }

    internal sealed class ProbeProvider(string name) : IPaymentGatewayProvider
    {
        public string Name => name;
        public int Calls { get; private set; }
        public bool Reject { get; init; }
        public Task<Result<GatewayLink>> CreateLinkAsync(GatewayLinkRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Reject ? Result.Failure<GatewayLink>(PaymentGatewayErrors.Unavailable)
                : Result.Success(new GatewayLink("token", new(new Uri("https://example.test/pay"), GatewayRedirectMethod.Get, new Dictionary<string, string>()))));
        }
        public Task<Result<GatewayCallbackResult>> VerifyCallbackAsync(GatewayCallbackRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
}
