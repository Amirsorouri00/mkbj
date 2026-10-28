using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Dtos;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Endpoints;

public sealed class CustomersEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customers").WithTags("Customers");

        group.MapPost("/registrations", RegisterCustomer)
                .WithName("registrations")
                .WithDescription("add new customer by mobile number and identityUserId")
                .AllowAnonymous();

        group.MapPost("/registrations/submit", RegisterCustomerInfo)
                .WithName("registrations/submit")
                .WithDescription("update profile info and validation shahkar")
                .RequireAuthorization();

        group.MapPut("/me", Profile)
                .WithName("me")
                .WithDescription("update profile info")
                .Produces<Result<int>>(StatusCodes.Status200OK)
                .Produces<Result<int>>(StatusCodes.Status400BadRequest)
                .Produces<Result<int>>(StatusCodes.Status401Unauthorized)
                .Produces<Result<int>>(StatusCodes.Status409Conflict)
                .Produces<Result<int>>(StatusCodes.Status500InternalServerError)
                .RequireAuthorization();

        group.MapGet("/me", GetProfile)
                .WithName("get-me")
                .WithDescription("get the authenticated customer profile")
                .Produces<Result<CustomerProfile>>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status401Unauthorized)
                .Produces(StatusCodes.Status404NotFound)
                .RequireAuthorization();
    }

    private static async Task<IResult> RegisterCustomerInfo(
            RegisterCustomerInfoRequest req,
            ICustomerInboundPort svc,
            IUserProfileService userProfileService,
            CancellationToken ct)
    {
        var r = await svc.RegisterCustomerInfoAsync(identityUserId: userProfileService.IdentityUserId,
            nationalCode: req.nationalCode,
            firstName: req.firstName,
            lastName: req.lastName,
            gender: req.gender,
            birthDate: req.birthDate,
            cityId: req.cityId,
            districtId: req.districtId,
            address:req.address,
             ct);
        return r.ToHttpResult(new CustomerCreatedResponse(userProfileService.IdentityUserId));
    }

    private static async Task<IResult> RegisterCustomer(
            Guid identityUserId,
            string mobileNumber,
            ICustomerInboundPort svc,
            CancellationToken ct)
    {
        if (identityUserId == Guid.Empty)
            return EndpointResult.BadRequest("identityUserId is required.");

        if (string.IsNullOrWhiteSpace(mobileNumber))
            return EndpointResult.BadRequest("mobileNumber is required.");

        var r = await svc.RegisterAsync(identityUserId: identityUserId,
            mobileNumber: mobileNumber,
             ct);
        return r.ToHttpResult(_ => new CustomerCreatedResponse(identityUserId));
    }

    private static async Task<IResult> Profile(
            ProfileRequest req,
            ICustomerInboundPort svc,
            IUserProfileService userProfileService,
            CancellationToken ct)
    {
        var r = await svc.UpdateProfileAsync(identityUserId: userProfileService.IdentityUserId,
            firstName: req.firstName,
            lastName: req.lastName,
            gender: req.gender,
            birthDate: req.birthDate,
             ct);

        return r.ToCreatedHttpResult($"/api/v1/customers/{userProfileService.IdentityUserId}", new CustomerCreatedResponse(userProfileService.IdentityUserId));
    }

    private static async Task<IResult> GetProfile(
            ICustomerInboundPort svc,
            IUserProfileService userProfileService,
            CancellationToken ct)
    {
        var result = await svc.GetProfileAsync(userProfileService.IdentityUserId, ct);
        return result.ToHttpResult();
    }
}
