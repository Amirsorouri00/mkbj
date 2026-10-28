namespace EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;

public sealed record AttributeBlueprintFilter(
    string? NameContains = null,
    AttributeBlueprintStatusFilter? Status = null,
    AttributeBlueprintDataTypeFilter? DataType = null);

public enum AttributeBlueprintDataTypeFilter
{
    Text = 1,
    Number = 2,
    Boolean = 3,
    Date = 4
}

public enum AttributeBlueprintStatusFilter
{
    Active = 1,
    Inactive = 2,
    Archived = 3
}

