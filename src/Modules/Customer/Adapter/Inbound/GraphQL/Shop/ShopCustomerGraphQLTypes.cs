using Mkbj.Modules.Customer.QueryApplication.Customers;

namespace Mkbj.Modules.Customer.Adapter.Inbound.GraphQL.Shop;

public sealed class ShopCustomerProfileType : ObjectType<CustomerProfileView>
{
    protected override void Configure(IObjectTypeDescriptor<CustomerProfileView> descriptor) =>
        descriptor.Field(customer => customer.Id).Type<NonNullType<IdType>>();
}
