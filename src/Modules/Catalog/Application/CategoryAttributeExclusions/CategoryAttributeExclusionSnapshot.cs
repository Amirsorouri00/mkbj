namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions;

public sealed record CategoryAttributeExclusionSnapshot(
    long CategoryId,
    long SourceAssignmentId,
    bool IsExcluded);
