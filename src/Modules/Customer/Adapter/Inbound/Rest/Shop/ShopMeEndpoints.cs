using System.Security.Claims;

using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using EwaysVNext.Modules.Customer.QueryApplication.Customers;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Shop;

/// <summary>
/// A website customer's own profile.
/// <para>
/// Both routes ensure the customer exists before doing anything else. Nothing in Keycloak tells
/// this system that someone registered, so the first authenticated request is the first moment
/// the record can be created — and a webhook would be one more thing that can silently stop
/// firing.
/// </para>
/// </summary>
public sealed class ShopMeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/shop")
            .WithTags("Shop")
            .RequireAuthorization(AuthPolicies.ShopCustomer);

        group.MapGet("/me", async (
            ClaimsPrincipal user,
            IWebsiteCustomerPort website,
            ICustomerShopQueries queries,
            CancellationToken ct) =>
        {
            var ensured = await Ensure(user, website, ct);

            return ensured.IsFailure
                ? EndpointResult.Fail(ensured.Error!)
                : (await queries.GetProfileAsync(ensured.Value!.CustomerId, ct)).ToHttpResult();
        });

        group.MapPut("/me", async (
            UpdateProfileRequest request,
            ClaimsPrincipal user,
            ICorrelationContext correlation,
            IWebsiteCustomerPort website,
            ICustomerProfileService profiles,
            ICustomerShopQueries queries,
            CancellationToken ct) =>
        {
            var ensured = await Ensure(user, website, ct);

            if (ensured.IsFailure)
                return EndpointResult.Fail(ensured.Error!);

            var customerId = ensured.Value!.CustomerId;

            var updated = await profiles.UpdateProfileAsync(
                customerId,
                new UpdateProfileCommand(
                    request.FullName,
                    request.CompanyName,
                    request.Phone,
                    request.BillingAddress is null
                        ? null
                        : new AddressSnapshot(
                            request.BillingAddress.Line1,
                            request.BillingAddress.Line2,
                            request.BillingAddress.City,
                            request.BillingAddress.PostalCode,
                            request.BillingAddress.CountryIso2),
                    request.RegistrationNumber),
                correlation.CorrelationId,
                ct);

            // The profile is returned on success so a client does not have to follow up with a
            // GET to see what it now holds.
            return updated.IsFailure
                ? EndpointResult.Fail(updated.Error!)
                : (await queries.GetProfileAsync(customerId, ct)).ToHttpResult();
        });
    }

    private static async Task<Result<WebsiteCustomerRef>> Ensure(
        ClaimsPrincipal user, IWebsiteCustomerPort website, CancellationToken ct)
    {
        if (!ShopIdentity.TryRead(user, out var subject, out var email, out var emailVerified))
            return Result.Failure<WebsiteCustomerRef>(Error.Forbidden);

        return await website.EnsureFromIdentityAsync(subject, email, emailVerified, ct);
    }
}
