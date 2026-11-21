using System.Security.Claims;

using Mkbj.Core.Application;
using Mkbj.Modules.Customer.Adapter.Inbound.Rest.Shop;
using Mkbj.Modules.Customer.Application.Customers.Ports.Inbound;
using Mkbj.Modules.Customer.QueryApplication.Customers;
using Mkbj.Outcomes;
using Mkbj.Presentation.Endpoint.GraphQL;
using Mkbj.Presentation.Endpoint.GraphQL.Extensions;
using Mkbj.Presentation.Endpoint.Security;
using HotChocolate.Authorization;

namespace Mkbj.Modules.Customer.Adapter.Inbound.GraphQL.Shop;

public sealed record UpdateProfileInput(
    string? FullName,
    string? CompanyName,
    string? Phone,
    AddressSnapshot? BillingAddress,
    string? RegistrationNumber);

[ExtendObjectType(typeof(Query))]
public sealed class ShopMeQueries
{
    [Authorize(Policy = AuthPolicies.ShopCustomer)]
    public async Task<CustomerProfileView> MeAsync(
        ClaimsPrincipal user,
        [Service] IWebsiteCustomerPort website,
        [Service] ICustomerShopQueries queries,
        CancellationToken ct)
    {
        var customerId = await ShopGraphQLSupport.EnsureAsync(user, website, ct);

        return ResultGraphQLExtensions.ValueOrThrow(await queries.GetProfileAsync(customerId, ct));
    }
}

[ExtendObjectType(typeof(Mutation))]
public sealed class ShopProfileMutations
{
    [Authorize(Policy = AuthPolicies.ShopCustomer)]
    public async Task<CustomerProfileView> UpdateProfileAsync(
        UpdateProfileInput input,
        ClaimsPrincipal user,
        [Service] ICorrelationContext correlation,
        [Service] IWebsiteCustomerPort website,
        [Service] ICustomerProfileService profiles,
        [Service] ICustomerShopQueries queries,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        var customerId = await ShopGraphQLSupport.EnsureAsync(user, website, ct);

        (await profiles.UpdateProfileAsync(
            customerId,
            new UpdateProfileCommand(
                input.FullName, input.CompanyName, input.Phone, input.BillingAddress, input.RegistrationNumber),
            correlation.CorrelationId,
            ct)).ThrowIfFailure();

        return ResultGraphQLExtensions.ValueOrThrow(await queries.GetProfileAsync(customerId, ct));
    }
}

internal static class ShopGraphQLSupport
{
    /// <summary>
    /// Shared so the two protocols cannot drift on who the caller is or when the record is
    /// created — the same mistake the permission paths already made once.
    /// </summary>
    public static async Task<long> EnsureAsync(
        ClaimsPrincipal user, IWebsiteCustomerPort website, CancellationToken ct)
    {
        if (!ShopIdentity.TryRead(user, out var subject, out var email, out var emailVerified))
            throw Mkbj.Outcomes.Error.Forbidden.ToGraphQLException();

        var ensured = await website.EnsureFromIdentityAsync(subject, email, emailVerified, ct);

        return ResultGraphQLExtensions.ValueOrThrow(ensured).CustomerId;
    }
}
