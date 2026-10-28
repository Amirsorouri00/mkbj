using System.Globalization;
using EwaysVNext.Domain.Common;
using HotChocolate.AspNetCore;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EwaysVNext.Presentation.Endpoint.GraphQL;

public static class DependencyInjection
{
    public static IRequestExecutorBuilder AddGraphQLBootstrap(this IServiceCollection services)
    {
        var builder = services.AddGraphQLServer();

        if (services.Any(d => d.ServiceType == typeof(GraphQLBootstrapMarker)))
            return builder;

        services.TryAddSingleton<GraphQLBootstrapMarker>();
        // GraphQLErrorFilter reads the correlation id off the current request; registering it here
        // keeps the bootstrap self-contained instead of depending on a module having called
        // AddUserProfileService first.
        services.AddHttpContextAccessor();
        builder
            .AddQueryType<Query>()
            .AddMutationType<Mutation>()
            .AddAuthorization()
            .BindRuntimeType<SnowflakeId, IdType>()
            .AddTypeConverter<string, SnowflakeId>(value => long.Parse(value, CultureInfo.InvariantCulture))
            .AddTypeConverter<long, SnowflakeId>(value => value)
            .AddTypeConverter<SnowflakeId, string>(id => id.ToString())
            .AddErrorFilter<GraphQLErrorFilter>()
            .AddHttpResponseFormatter<GraphQLHttpResponseFormatter>()
            .ModifyCostOptions(options => options.MaxFieldCost = 20000);

        return builder;
    }

    public static WebApplication MapGraphQLEndpoint(this WebApplication app, Action<GraphQLServerOptions>? configureTool = null)
    {
        var endpoint = app.MapGraphQL();

        if (configureTool is not null)
            endpoint.WithOptions(configureTool);

        return app;
    }

    private sealed class GraphQLBootstrapMarker;
}
