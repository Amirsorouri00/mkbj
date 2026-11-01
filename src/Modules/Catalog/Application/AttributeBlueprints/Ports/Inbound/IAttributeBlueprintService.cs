using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;


namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;

public interface IAttributeBlueprintService
{
    Task<Result<long>> CreateAsync(string name, AttributeBlueprintDataType dataType,
        IReadOnlyCollection<AttributeBlueprintOptionInput> options, CancellationToken ct = default);

    Task<Result> UpdateAsync(long id, string name, AttributeBlueprintDataType dataType, bool isActive,
        IReadOnlyCollection<UpdateAttributeBlueprintOptionInput> options, CancellationToken ct = default);

    Task<Result> RenameAsync(long id, string name, CancellationToken ct = default);

    Task<Result> ChangeDataTypeAsync(long id, AttributeBlueprintDataType dataType, CancellationToken ct = default);

    Task<Result> ActivateAsync(long id, CancellationToken ct = default);

    Task<Result> DeactivateAsync(long id, CancellationToken ct = default);

    Task<Result> ArchiveAsync(long id, CancellationToken ct = default);

    Task<Result> UnarchiveAsync(long id, CancellationToken ct = default);

    Task<Result> DeleteAsync(long id, CancellationToken ct = default);

    Task<Result<long>> AddOptionAsync(long attributeBlueprintId, string value, long? measurementUnitId,
        CancellationToken ct = default);

    Task<Result> UpdateOptionAsync(long attributeBlueprintId, long optionId, string value, long? measurementUnitId,
        CancellationToken ct = default);

    Task<Result> RemoveOptionAsync(long attributeBlueprintId, long optionId, CancellationToken ct = default);

    /// <summary>
    /// Narrows the attribute to one category, or widens it back to the whole catalogue.
    /// </summary>
    Task<Result> SetScopeAsync(long id, AttributeScope scope, long? categoryId, CancellationToken ct = default);

    /// <summary>
    /// Replaces the attribute's alias list — the supplier column headings that resolve to it.
    /// </summary>
    Task<Result> SetAliasesAsync(long id, IReadOnlyCollection<string> aliases, CancellationToken ct = default);

    /// <summary>
    /// Sets or clears the attribute's default measurement unit.
    /// </summary>
    Task<Result> SetUnitAsync(long id, long? unitId, CancellationToken ct = default);
}
