using EwaysVNext.Modules.Catalog.QueryApplication.Products;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Products;

// Snowflake ids are 64-bit and exceed what a JSON number can carry exactly, so every one of them
// is an ID (a string on the wire) rather than an Int.

public sealed class ProductAdminRowType : ObjectType<ProductAdminRowDto>
{
    protected override void Configure(IObjectTypeDescriptor<ProductAdminRowDto> descriptor)
    {
        descriptor.Field(row => row.Id).Type<NonNullType<IdType>>();
        descriptor.Field(row => row.BrandId).Type<NonNullType<IdType>>();
        descriptor.Field(row => row.PrimaryCategoryId).Type<NonNullType<IdType>>();
        descriptor.Field(row => row.MergedIntoProductId).Type<IdType>();
    }
}

public sealed class ProductAdminDetailType : ObjectType<ProductAdminDetailDto>
{
    protected override void Configure(IObjectTypeDescriptor<ProductAdminDetailDto> descriptor)
    {
        descriptor.Field(detail => detail.Id).Type<NonNullType<IdType>>();
        descriptor.Field(detail => detail.BrandId).Type<NonNullType<IdType>>();
        descriptor.Field(detail => detail.PrimaryCategoryId).Type<NonNullType<IdType>>();
        descriptor.Field(detail => detail.MergedIntoProductId).Type<IdType>();
    }
}

public sealed class ProductAdminVariantType : ObjectType<ProductAdminVariantDto>
{
    protected override void Configure(IObjectTypeDescriptor<ProductAdminVariantDto> descriptor)
        => descriptor.Field(variant => variant.Id).Type<NonNullType<IdType>>();
}

public sealed class ProductAdminIdentifierType : ObjectType<ProductAdminIdentifierDto>
{
    protected override void Configure(IObjectTypeDescriptor<ProductAdminIdentifierDto> descriptor)
        => descriptor.Field(identifier => identifier.Id).Type<NonNullType<IdType>>();
}

public sealed class ProductAdminMediaType : ObjectType<ProductAdminMediaDto>
{
    protected override void Configure(IObjectTypeDescriptor<ProductAdminMediaDto> descriptor)
        => descriptor.Field(media => media.Id).Type<NonNullType<IdType>>();
}
