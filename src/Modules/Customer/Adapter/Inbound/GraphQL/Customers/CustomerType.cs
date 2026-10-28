using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

public sealed class CustomerType : ObjectType<CustomerAdminListItem>
{
    protected override void Configure(IObjectTypeDescriptor<CustomerAdminListItem> descriptor)
    {
        descriptor.Name("Customer");

        descriptor.Field(c => c.FirstName).Name("name");
        descriptor.Field(c => c.LastName).Name("family");
        descriptor.Field(c => c.NationalCode).Name("nationalId");
        descriptor.Field(c => c.MobileNumber).Name("phoneNumber");
        descriptor.Field(c => c.DistrictId).Name("regionId");
        descriptor.Field(c => c.DistrictName).Name("regionName");

        descriptor.Field("userCode")
            .Type<NonNullType<StringType>>()
            .Resolve(ctx => ctx.Parent<CustomerAdminListItem>().Id.ToString());

        descriptor.Field("registrationStatus")
            .Type<EnumType<CustomerStatusType>>()
            .Resolve(ctx => (CustomerStatusType?)ctx.Parent<CustomerAdminListItem>().StatusId);

        descriptor.Field("verificationStatus")
            .Type<EnumType<VerificationStatusType>>()
            .Resolve(ctx => (VerificationStatusType?)ctx.Parent<CustomerAdminListItem>().VerificationStatusId);

        descriptor.Field("verificationMethodType")
            .Type<EnumType<VerificationMethodType>>()
            .Resolve(ctx => (VerificationMethodType?)ctx.Parent<CustomerAdminListItem>().VerificationMethod);

        descriptor.Ignore(c => c.StatusId);
        descriptor.Ignore(c => c.VerificationStatusId);
        descriptor.Ignore(c => c.VerificationMethod);
    }
}
