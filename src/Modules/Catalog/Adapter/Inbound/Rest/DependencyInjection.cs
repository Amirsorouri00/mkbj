using Microsoft.Extensions.DependencyInjection;
using Mkbj.Presentation.Endpoint;
using Mkbj.Modules.Catalog.Adapter.Inbound.Rest.Categories;

namespace Mkbj.Modules.Catalog.Adapter.Inbound.Rest;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogRestAdapter(this IServiceCollection services)
    {
        services.AddEndpoints(typeof(CategoryPublicEndpoints).Assembly);
        services.AddSingleton<CategoryPublicEndpoints>();
        return services;
    }
}
