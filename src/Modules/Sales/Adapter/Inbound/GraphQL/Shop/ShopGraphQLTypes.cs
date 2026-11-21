using System.Globalization;
using Mkbj.Modules.Sales.QueryApplication.Partner;
using Mkbj.Modules.Sales.QueryApplication.Shop;

namespace Mkbj.Modules.Sales.Adapter.Inbound.GraphQL.Shop;

public sealed class ShopCartLineType : ObjectType<CartLineGraph>
{
    protected override void Configure(IObjectTypeDescriptor<CartLineGraph> descriptor) =>
        descriptor.Field(line => line.VariantId).Type<NonNullType<IdType>>();
}

public sealed class ShopCartChangeType : ObjectType<CartChangeGraph>
{
    protected override void Configure(IObjectTypeDescriptor<CartChangeGraph> descriptor) =>
        descriptor.Field(change => change.VariantId).Type<NonNullType<IdType>>();
}

public sealed class ShopOrderType : ObjectType<ShopOrderDto>
{
    protected override void Configure(IObjectTypeDescriptor<ShopOrderDto> descriptor)
    {
        descriptor.Field(order => order.Id).Type<NonNullType<IdType>>();
        descriptor.Field(order => order.TotalMinor)
            .Type<NonNullType<StringType>>()
            .Resolve(context => context.Parent<ShopOrderDto>().TotalMinor.ToString(CultureInfo.InvariantCulture));
    }
}

public sealed class ShopOrderLineType : ObjectType<OrderLineDto>
{
    protected override void Configure(IObjectTypeDescriptor<OrderLineDto> descriptor)
    {
        descriptor.Field(line => line.VariantId).Type<NonNullType<IdType>>();
        descriptor.Field(line => line.ProductId).Type<NonNullType<IdType>>();
        descriptor.Field(line => line.UnitPriceMinor)
            .Type<NonNullType<StringType>>()
            .Resolve(context => context.Parent<OrderLineDto>().UnitPriceMinor.ToString(CultureInfo.InvariantCulture));
        descriptor.Field(line => line.LineTotalMinor)
            .Type<NonNullType<StringType>>()
            .Resolve(context => context.Parent<OrderLineDto>().LineTotalMinor.ToString(CultureInfo.InvariantCulture));
        descriptor.Field(line => line.ReferencePriceMinor)
            .Type<NonNullType<StringType>>()
            .Resolve(context => context.Parent<OrderLineDto>().ReferencePriceMinor.ToString(CultureInfo.InvariantCulture));
    }
}

public sealed class ShopDocumentType : ObjectType<DocumentDto>
{
    protected override void Configure(IObjectTypeDescriptor<DocumentDto> descriptor)
    {
        descriptor.Field(document => document.Id).Type<NonNullType<IdType>>();
        descriptor.Field(document => document.RelatedDocumentId).Type<IdType>();
    }
}

public sealed class ShopOrderPageType : ObjectType<CursorPage<ShopOrderDto>>
{
    protected override void Configure(IObjectTypeDescriptor<CursorPage<ShopOrderDto>> descriptor) =>
        descriptor.Field(page => page.NextCursor).Type<IdType>();
}
