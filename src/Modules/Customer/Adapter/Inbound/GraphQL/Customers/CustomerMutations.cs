using EwaysVNext.Core.Application;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

[ExtendObjectType(typeof(Mutation))]
public sealed class CustomerMutations
{
    public async Task<UpdateCustomerStatusPayload> UpdateCustomerStatus(
        UpdateCustomerStatusInput input,
        [Service] ICustomerInboundPort port,
        [Service] IUserProfileService userProfileService,
        CancellationToken cancellationToken)
    {
        var result = await port.UpdateStatusAsync(
            input.CustomerId, input.Status, userProfileService.IdentityUserId, input.Reason, cancellationToken);

        return result.ValueOrThrow(new UpdateCustomerStatusPayload(input.CustomerId, input.Status, true));
    }

    public async Task<UpdateCustomerPayload> UpdateCustomer(
        UpdateCustomerInput input,
        [Service] ICustomerInboundPort port,
        CancellationToken cancellationToken)
    {
        var address = input.Address is { } a
            ? new AdminCustomerAddressUpdate(a.ProvinceId, a.CityId, a.DistrictId, a.AddressText, a.PostalCode)
            : null;

        var result = await port.UpdateCustomerByAdminAsync(
            input.CustomerId,
            new AdminCustomerUpdate(
                input.Gender,
                input.EconomicCode,
                input.IntaCode,
                input.RoleCode,
                input.Email,
                input.SecondaryPhoneNumber,
                input.JobTypeIds ?? [],
                address),
            cancellationToken);

        return result.ValueOrThrow(new UpdateCustomerPayload(input.CustomerId, true));
    }

    public async Task<UpdateCustomerVerificationStatusPayload> UpdateCustomerVerificationStatus(
        UpdateCustomerVerificationStatusInput input,
        [Service] ICustomerInboundPort port,
        [Service] IUserProfileService userProfileService,
        CancellationToken cancellationToken)
    {
        var result = await port.UpdateVerificationStatusAsync(
            input.CustomerId, 0, input.Status, userProfileService.IdentityUserId, input.Reason, cancellationToken);

        return result.ValueOrThrow(new UpdateCustomerVerificationStatusPayload(input.CustomerId, input.Status, true));
    }
}
