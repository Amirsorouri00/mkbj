namespace EwaysVNext.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

public sealed record EffectiveCategoryAttributeSnapshot(
    long AssignmentId,
    long SourceCategoryId,
    long AttributeBlueprintId,
    string AttributeBlueprintName,
    bool IsInherited,
    bool IsExcluded,
    bool IsRequired,
    bool IsFilterable,
    bool IsSearchable,
    bool IsInTitle,
    int? TitleOrder,
    bool IsPrimary,
    bool IsVariantGenerating);
