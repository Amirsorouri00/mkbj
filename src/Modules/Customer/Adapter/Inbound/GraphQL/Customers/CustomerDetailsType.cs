using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using System.Globalization;
using CustomerAggregate = EwaysVNext.Modules.Customer.Application.Domain.Customers.Customer;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Customers;

public sealed class CustomerDetailsType : ObjectType<CustomerAdminDetails>
{
    protected override void Configure(IObjectTypeDescriptor<CustomerAdminDetails> descriptor)
    {
        descriptor.Name("CustomerDetails");

        descriptor.Field(c => c.FirstName).Name("name");
        descriptor.Field(c => c.LastName).Name("family");
        descriptor.Field(c => c.NationalCode).Name("nationalId");
        descriptor.Field(c => c.MobileNumber).Name("phoneNumber");
        descriptor.Field(c => c.DistrictId).Name("regionId");
        descriptor.Field(c => c.DistrictName).Name("regionName");

        descriptor.Field("userCode")
            .Type<NonNullType<StringType>>()
            .Resolve(ctx => ctx.Parent<CustomerAdminDetails>().Id.ToString(CultureInfo.InvariantCulture));

        descriptor.Field("registrationStatus")
            .Type<EnumType<CustomerStatusType>>()
            .Resolve(ctx => (CustomerStatusType?)ctx.Parent<CustomerAdminDetails>().StatusId);

        descriptor.Field("verificationStatus")
            .Type<EnumType<VerificationStatusType>>()
            .Resolve(ctx => (VerificationStatusType?)ctx.Parent<CustomerAdminDetails>().VerificationStatusId);

        descriptor.Field("verificationMethodType")
            .Type<EnumType<VerificationMethodType>>()
            .Resolve(ctx => (VerificationMethodType?)ctx.Parent<CustomerAdminDetails>().VerificationMethod);

        descriptor.Field("canEdit")
            .Type<NonNullType<BooleanType>>()
            .Resolve(ctx => CustomerAggregate.CanBeEditedByAdmin(ctx.Parent<CustomerAdminDetails>().StatusId));

        descriptor.Ignore(c => c.StatusId);
        descriptor.Ignore(c => c.VerificationStatusId);
        descriptor.Ignore(c => c.VerificationMethod);
    }
}
