using Mkbj.Modules.Catalog.Contracts.Categories;
using Mkbj.Modules.Catalog.QueryApplication.Storefront;

namespace Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL.Shop;

public sealed class ShopProductCardType : ObjectType<ProductCardDto>
{
    protected override void Configure(IObjectTypeDescriptor<ProductCardDto> descriptor)
    {
        descriptor.Field(product => product.ProductId).Type<NonNullType<IdType>>();
        descriptor.Field(product => product.BrandId).Type<NonNullType<IdType>>();
        descriptor.Field(product => product.CategoryId).Type<NonNullType<IdType>>();
    }
}

public sealed class ShopCategoryTileType : ObjectType<CategoryTileDto>
{
    protected override void Configure(IObjectTypeDescriptor<CategoryTileDto> descriptor) =>
        descriptor.Field(category => category.CategoryId).Type<NonNullType<IdType>>();
}

public sealed class ShopCategoryCrumbType : ObjectType<CategoryCrumbDto>
{
    protected override void Configure(IObjectTypeDescriptor<CategoryCrumbDto> descriptor) =>
        descriptor.Field(category => category.CategoryId).Type<NonNullType<IdType>>();
}

public sealed class ShopVariantOfferType : ObjectType<VariantOfferDto>
{
    protected override void Configure(IObjectTypeDescriptor<VariantOfferDto> descriptor) =>
        descriptor.Field(variant => variant.VariantId).Type<NonNullType<IdType>>();
}

public sealed class ShopProductDetailType : ObjectType<ProductDetailDto>
{
    protected override void Configure(IObjectTypeDescriptor<ProductDetailDto> descriptor)
    {
        descriptor.Field(product => product.ProductId).Type<NonNullType<IdType>>();
        descriptor.Field(product => product.BrandId).Type<NonNullType<IdType>>();
    }
}

public sealed class ShopCategoryTreeNodeType : ObjectType<CategoryTreeNodeDto>
{
    protected override void Configure(IObjectTypeDescriptor<CategoryTreeNodeDto> descriptor)
    {
        descriptor.Field(category => category.Id).Type<NonNullType<IdType>>();
        descriptor.Field(category => category.ParentId).Type<IdType>();
    }
}
