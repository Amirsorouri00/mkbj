using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Dtos;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Endpoints;

public sealed class CustomerAddressesEndpoints : IEndpoint
{
    private const string BasePath = "/api/v1/customers/me/addresses";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(BasePath).WithTags("Customer Addresses").RequireAuthorization();

        group.MapGet("/", GetAddresses)
            .WithName("get-my-addresses")
            .WithDescription("list the authenticated customer's addresses; isPrimary marks the primary/default one")
            .Produces<IReadOnlyList<CustomerAddressDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:long}", GetAddress)
            .WithName("get-my-address")
            .WithDescription("get one of the authenticated customer's addresses")
            .Produces<CustomerAddressDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAddress)
            .WithName("create-my-address")
            .WithDescription("add a (non-primary) address for the authenticated customer")
            .Produces<CustomerAddressDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:long}", UpdateAddress)
            .WithName("update-my-address")
            .WithDescription("edit a non-primary address; the primary address cannot be edited")
            .Produces<CustomerAddressDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:long}", DeleteAddress)
            .WithName("delete-my-address")
            .WithDescription("delete a non-primary address; the primary address cannot be deleted")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{id:long}/default", SetDefaultAddress)
            .WithName("set-my-default-address")
            .WithDescription("make this address the primary/default delivery address; the previous one is demoted")
            .Produces<CustomerAddressDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetAddresses(
        ICustomerAddressInboundPort svc, IUserProfileService user, CancellationToken ct)
        => (await svc.GetAddressesAsync(user.IdentityUserId, ct)).ToHttpResult();

    private static async Task<IResult> GetAddress(
        long id, ICustomerAddressInboundPort svc, IUserProfileService user, CancellationToken ct)
        => (await svc.GetAddressAsync(user.IdentityUserId, id, ct)).ToHttpResult();

    private static async Task<IResult> CreateAddress(
        CustomerAddressRequest req, ICustomerAddressInboundPort svc, IUserProfileService user, CancellationToken ct)
    {
        var r = await svc.CreateAddressAsync(user.IdentityUserId, ToInput(req), ct);
        return r.ToCreatedHttpResult(a => $"{BasePath}/{a.Id}");
    }

    private static async Task<IResult> UpdateAddress(
        long id, CustomerAddressRequest req, ICustomerAddressInboundPort svc, IUserProfileService user, CancellationToken ct)
        => (await svc.UpdateAddressAsync(user.IdentityUserId, id, ToInput(req), ct)).ToHttpResult();

    private static async Task<IResult> DeleteAddress(
        long id, ICustomerAddressInboundPort svc, IUserProfileService user, CancellationToken ct)
        => (await svc.DeleteAddressAsync(user.IdentityUserId, id, ct)).ToHttpResult();

    private static async Task<IResult> SetDefaultAddress(
        long id, ICustomerAddressInboundPort svc, IUserProfileService user, CancellationToken ct)
        => (await svc.SetDefaultAddressAsync(user.IdentityUserId, id, ct)).ToHttpResult();

    private static CustomerAddressInput ToInput(CustomerAddressRequest req) => new(
        req.cityId, req.districtId, req.addressText, req.postalCode,
        req.isSelfRecipient, req.recipientName, req.recipientMobile);
}
