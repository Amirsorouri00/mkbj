using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Database;
using EwaysVNext.DependencyInjection;
using EwaysVNext.Modules.Customer.Adapter.Outbound.Repositories;
using EwaysVNext.Modules.Customer.Adapter.Outbound.Storage;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;

namespace EwaysVNext.Modules.Customer.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerOutboundAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ITeamMemberRepository, TeamMemberRepository>();
        services.AddSingleton<ILegalInformationDocumentStorage, LocalFileSystemLegalInformationDocumentStorage>();

        services
            .AddPersistenceInterceptors()
            .AddUnitOfWork<EwaysVNextDbContext>()
            .AddEvents(typeof(CustomerAggregate).Assembly);

        return services;
    }
}
