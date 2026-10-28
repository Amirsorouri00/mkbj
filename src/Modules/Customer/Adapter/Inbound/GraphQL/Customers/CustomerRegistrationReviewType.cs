using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

public sealed class CustomerRegistrationReviewType : ObjectType<CustomerRegistrationReview>
{
    protected override void Configure(IObjectTypeDescriptor<CustomerRegistrationReview> descriptor)
    {
        descriptor.Name("CustomerRegistrationReview");

        descriptor.Field("verificationStatus")
            .Type<EnumType<CustomerStatusType>>()
            .Resolve(ctx => (CustomerStatusType?)ctx.Parent<CustomerRegistrationReview>().StatusId);

        descriptor.Field("verificationMethod")
            .Type<EnumType<VerificationMethodType>>()
            .Resolve(ctx => (VerificationMethodType?)ctx.Parent<CustomerRegistrationReview>().VerificationMethod);

        descriptor.Ignore(r => r.StatusId);
        descriptor.Ignore(r => r.VerificationMethod);
    }
}
