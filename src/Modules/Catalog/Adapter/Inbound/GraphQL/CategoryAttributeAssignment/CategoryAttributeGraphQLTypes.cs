using Mkbj.Modules.Catalog.Application.CategoryAttributeExclusions;
using Mkbj.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

namespace Mkbj.Modules.Catalog.Adapter.Inbound.GraphQL.CategoryAttributeAssignment;

// Snowflake ids must remain strings on the JSON wire. Leaving one of these fields as GraphQL
// Long silently rounds it in a browser before it is used by a later mutation.
public sealed class CategoryAttributeAssignmentSnapshotType
    : ObjectType<CategoryAttributeAssignmentSnapshot>
{
    protected override void Configure(
        IObjectTypeDescriptor<CategoryAttributeAssignmentSnapshot> descriptor)
    {
        descriptor.Field(item => item.Id).Type<NonNullType<IdType>>();
        descriptor.Field(item => item.CategoryId).Type<NonNullType<IdType>>();
        descriptor.Field(item => item.AttributeBlueprintId).Type<NonNullType<IdType>>();
    }
}

public sealed class CategoryAttributeAssignmentAdminRowType
    : ObjectType<CategoryAttributeAssignmentAdminRow>
{
    protected override void Configure(
        IObjectTypeDescriptor<CategoryAttributeAssignmentAdminRow> descriptor)
    {
        descriptor.Field(item => item.Id).Type<NonNullType<IdType>>();
        descriptor.Field(item => item.CategoryId).Type<NonNullType<IdType>>();
        descriptor.Field(item => item.AttributeBlueprintId).Type<NonNullType<IdType>>();
    }
}

public sealed class EffectiveCategoryAttributeSnapshotType
    : ObjectType<EffectiveCategoryAttributeSnapshot>
{
    protected override void Configure(
        IObjectTypeDescriptor<EffectiveCategoryAttributeSnapshot> descriptor)
    {
        descriptor.Field(item => item.AssignmentId).Type<NonNullType<IdType>>();
        descriptor.Field(item => item.SourceCategoryId).Type<NonNullType<IdType>>();
        descriptor.Field(item => item.AttributeBlueprintId).Type<NonNullType<IdType>>();
    }
}

public sealed class CategoryAttributeExclusionSnapshotType
    : ObjectType<CategoryAttributeExclusionSnapshot>
{
    protected override void Configure(
        IObjectTypeDescriptor<CategoryAttributeExclusionSnapshot> descriptor)
    {
        descriptor.Field(item => item.CategoryId).Type<NonNullType<IdType>>();
        descriptor.Field(item => item.SourceAssignmentId).Type<NonNullType<IdType>>();
    }
}
