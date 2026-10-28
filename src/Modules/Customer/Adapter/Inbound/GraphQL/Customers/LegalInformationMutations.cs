using EwaysVNext.Core.Application;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

[ExtendObjectType(typeof(Mutation))]
public sealed class LegalInformationMutations
{
    [Authorize(Policy = "Admin")]
    public async Task<LegalInformationResponse> ApproveLegalInformation(
        long customerId,
        [Service] ILegalInformationInboundPort port,
        [Service] IUserProfileService userProfileService,
        CancellationToken cancellationToken)
    {
        var result = await port.ApproveLegalInformationAsync(customerId, userProfileService.IdentityUserId, cancellationToken);
        return result.ValueOrThrow();
    }

    [Authorize(Policy = "Admin")]
    public async Task<LegalInformationResponse> RejectLegalInformation(
        RejectLegalInformationInput input,
        [Service] ILegalInformationInboundPort port,
        [Service] IUserProfileService userProfileService,
        CancellationToken cancellationToken)
    {
        var editableFields = input.EditableFields.Aggregate(
            LegalInformationEditableField.None,
            (acc, field) => acc | field);

        var result = await port.RejectLegalInformationAsync(
            input.CustomerId, userProfileService.IdentityUserId, input.Reason, editableFields, cancellationToken);
        return result.ValueOrThrow();
    }
}
