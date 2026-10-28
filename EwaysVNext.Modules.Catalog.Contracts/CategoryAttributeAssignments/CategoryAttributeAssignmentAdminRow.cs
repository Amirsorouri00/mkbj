namespace EwaysVNext.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

public enum CategoryAttributePriorityGroup
{
    Standard = 0,
    Primary = 1,
    VariantGenerating = 2
}

public sealed record CategoryAttributeAssignmentAdminRow(
    long Id,
    long CategoryId,
    long AttributeBlueprintId,
    bool IsRequired,
    bool IsFilterable,
    bool IsSearchable,
    bool IsInTitle,
    int? TitleOrder,
    bool IsPrimary,
    bool IsVariantGenerating,
    CategoryAttributePriorityGroup PriorityGroup);
