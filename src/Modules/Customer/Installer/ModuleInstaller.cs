using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Customer;

public static class ModuleInstaller
{
    /// <summary>
    /// No-op: the Customer module was stripped to a shell when the Iran-market domain was removed
    /// (ADR-0022) and is rebuilt in phase 1 around the website/partner-linked customer model.
    /// The projects and this registration point stay so the Host wiring does not churn.
    /// </summary>
    public static IServiceCollection AddCustomerModule(this IServiceCollection services, IConfiguration config)
        => services;
}
