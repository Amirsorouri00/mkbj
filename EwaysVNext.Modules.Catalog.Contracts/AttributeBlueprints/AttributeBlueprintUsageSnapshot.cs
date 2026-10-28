namespace EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;

public sealed record AttributeBlueprintUsageSnapshot(long AttributeBlueprintId, int ProductValueCount, int CategoryMappingCount)
{
    public bool CanChangeDataType => ProductValueCount == 0;
    public bool CanDelete => ProductValueCount == 0;
}
