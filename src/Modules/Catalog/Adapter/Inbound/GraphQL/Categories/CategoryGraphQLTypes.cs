using EwaysVNext.Modules.Catalog.Contracts.Categories;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Categories;

public sealed class CategoryAdminType : ObjectType<CategoryAdminDto>
{
    protected override void Configure(IObjectTypeDescriptor<CategoryAdminDto> descriptor)
    {
        descriptor.Field(category => category.Id).Type<NonNullType<IdType>>();
        descriptor.Field(category => category.ParentId).Type<IdType>();
    }
}

public sealed class CategoryAdminParentType : ObjectType<CategoryAdminParentDto>
{
    protected override void Configure(IObjectTypeDescriptor<CategoryAdminParentDto> descriptor) =>
        descriptor.Field(parent => parent.Id).Type<NonNullType<IdType>>();
}

public sealed class CategoryAdminTreeNodeType : ObjectType<CategoryAdminTreeNodeDto>
{
    protected override void Configure(IObjectTypeDescriptor<CategoryAdminTreeNodeDto> descriptor)
    {
        descriptor.Field(category => category.Id).Type<NonNullType<IdType>>();
        descriptor.Field(category => category.ParentId).Type<IdType>();
    }
}
