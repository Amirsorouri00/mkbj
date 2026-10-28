using System;
using System.Collections.Generic;
using System.Text;

namespace EwaysVNext.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

public sealed record CategoryAttributeAssignmentSnapshot(
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
    bool IsRemoved);
