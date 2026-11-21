using Mkbj.Modules.Sales.Adapter.Outbound.Candidates;
using Mkbj.Modules.Sales.Adapter.Outbound.Documents;
using Mkbj.Modules.Sales.Adapter.Outbound.Gateways;
using Mkbj.Modules.Sales.Adapter.Outbound.Persistence;
using Mkbj.Modules.Sales.Application.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace Mkbj.Modules.Sales.Adapter.Outbound;

public static class DependencyInjection
{
    public static IServiceCollection AddSalesOutboundAdapters(this IServiceCollection services)
    {
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IDocumentNumberAllocator, DocumentNumberAllocator>();

        services.AddScoped<ISalesCatalogGateway, SalesCatalogGateway>();
        services.AddScoped<ISalesPricingGateway, SalesPricingGateway>();
        services.AddScoped<ISalesCustomerGateway, SalesCustomerGateway>();
        services.AddScoped<ISalesSettingsGateway, SalesSettingsGateway>();

        services.AddScoped<ICompositionAttemptRecorder, CompositionAttemptRecorder>();
        services.AddScoped<IPartnerOrderRequestJournal, PartnerOrderRequestJournal>();

        // The renderer holds no state and reads nothing but its argument.
        services.AddSingleton<IDocumentPdfRenderer, QuestPdfDocumentRenderer>();
        services.AddScoped<IDocumentStore, ObjectStorageDocumentStore>();
        services.AddSingleton<ICompositionBudgetFactory, StopwatchBudgetFactory>();
        services.AddSingleton<ICompositionSeedSource, RandomSeedSource>();

        // The cache outlives a request; the gateway that reads it does not.
        services.AddSingleton<CandidatePoolCache>();
        services.AddScoped<ICandidatePoolGateway, CachedCandidatePoolGateway>();

        return services;
    }
}
