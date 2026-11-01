using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Categories;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogRestAdapter(this IServiceCollection services)
    {
        services.AddEndpoints(typeof(CategoryPublicEndpoints).Assembly);
        services.AddSingleton<CategoryPublicEndpoints>();
        return services;
    }
}
