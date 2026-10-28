using Microsoft.Extensions.DependencyInjection.Extensions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Service;
using EwaysVNext.Modules.Customer.Application.Service.CompanyVerification;
using EwaysVNext.Modules.Customer.Application.Service.CompanyVerification.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Modules.Customer.Application.Service.Shahkar.Abstractions;
using Microsoft.Extensions.Configuration;

namespace EwaysVNext.Modules.Customer.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddScoped<ICustomerInboundPort, CustomerService>();
        services.AddScoped<ITeamMemberInboundPort, TeamMemberService>();
        services.AddScoped<ICustomerAddressInboundPort, CustomerAddressService>();
        services.AddScoped<ILegalInformationInboundPort, LegalInformationService>();
        services.TryAddScoped<ICompanyVerificationService, ItHubCompanyVerificationService>();
        services.AddShahkarServices(configuration);
        return services;
    }
}
