using EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;
using EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.TeamMembers;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerGraphQL(this IServiceCollection builder)
    {
        builder
            .AddGraphQLBootstrap()
            .AddType<TeamMemberQueries>()
            .AddType<TeamMemberMutations>()
            .AddType<TeamMemberType>()
            .AddType<CustomerQueries>()
            .AddType<CustomerType>()
            .AddType<CustomerMutations>()
            .AddType<CustomerDetailsQueries>()
            .AddType<CustomerDetailsType>()
            .AddType<JobTypeQueries>()
            .AddType<CustomerRegistrationReviewQueries>()
            .AddType<CustomerRegistrationReviewType>()
            .AddType<LegalInformationMutations>();

        return builder;
    }
}
