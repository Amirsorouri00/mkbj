using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways;

namespace EwaysVNext.Modules.Payment.GatewayTests;

public sealed class ParsianRegistrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Provider_is_registered_only_when_enabled(bool enabled)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddPaymentModule(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:Gateways:Parsian:Enabled"] = enabled.ToString(),
            ["Payment:Gateways:Parsian:LoginAccount"] = "test-only-credential"
        }).Build());
        using var sp = services.BuildServiceProvider();
        var result = sp.GetRequiredService<IPaymentGatewayProviderSelector>().SelectRetained("parsian");
        Assert.Equal(enabled, result.IsSuccess);
    }
}
