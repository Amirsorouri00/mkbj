using Mkbj.Modules.Catalog.QueryApplication.CategoryAttributeAssignments;

namespace Mkbj.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Inbound;

public interface ICategoryAttributeExclusionQueryService
{
    Task<IReadOnlyList<EffectiveCategoryAttributeSnapshot>> GetEffectiveAttributesAsync(long categoryId, CancellationToken ct = default);
}
