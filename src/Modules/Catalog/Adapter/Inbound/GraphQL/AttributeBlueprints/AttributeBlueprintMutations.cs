using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using HotChocolate.Authorization;
using ApplicationOptionInput = EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound.AttributeBlueprintOptionInput;
using ApplicationUpdateOptionInput = EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound.UpdateAttributeBlueprintOptionInput;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.AttributeBlueprints;

[ExtendObjectType(typeof(Mutation))]
public sealed class AttributeBlueprintMutations
{
    [Authorize(Policy = "Admin")]
    [GraphQLType(typeof(NonNullType<IdType>))]
    public async Task<long> CreateAttributeBlueprint(CreateAttributeBlueprintInput input, [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
        => (await blueprints.CreateAsync(input.Name, input.DataType,
            input.Options?.Select(option => new ApplicationOptionInput(option.Value, option.MeasurementUnitId)).ToArray() ?? [], ct)).ValueOrThrow();

    [Authorize(Policy = "Admin")]
    public async Task<bool> UpdateAttributeBlueprint(UpdateAttributeBlueprintInput input, [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
    {
        var options = input.Options.Select(option =>
            new ApplicationUpdateOptionInput(option.OptionId, option.Value, option.MeasurementUnitId)).ToArray();
        return (await blueprints.UpdateAsync(input.AttributeBlueprintId, input.Name, input.DataType, input.IsActive, options, ct)).ValueOrThrow(true);
    }

    [Authorize(Policy = "Admin")]
    [GraphQLType(typeof(NonNullType<IdType>))]
    public async Task<long> AddAttributeBlueprintOption(AddAttributeBlueprintOptionInput input, [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
        => (await blueprints.AddOptionAsync(input.AttributeBlueprintId, input.Value, input.MeasurementUnitId, ct)).ValueOrThrow();

    [Authorize(Policy = "Admin")]
    public async Task<bool> UpdateAttributeBlueprintOption(UpdateAttributeBlueprintOptionInput input, [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
        => (await blueprints.UpdateOptionAsync(input.AttributeBlueprintId, input.OptionId, input.Value, input.MeasurementUnitId, ct)).ValueOrThrow(true);

    [Authorize(Policy = "Admin")]
    public async Task<bool> RemoveAttributeBlueprintOption([GraphQLType(typeof(NonNullType<IdType>))] long attributeBlueprintId,
        [GraphQLType(typeof(NonNullType<IdType>))] long optionId, [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
        => (await blueprints.RemoveOptionAsync(attributeBlueprintId, optionId, ct)).ValueOrThrow(true);

    [Authorize(Policy = "Admin")]
    public async Task<bool> RenameAttributeBlueprint(long attributeBlueprintId, string newName, [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
        => (await blueprints.RenameAsync(attributeBlueprintId, newName, ct)).ValueOrThrow(true);

    [Authorize(Policy = "Admin")]
    public async Task<bool> ChangeAttributeBlueprintDataType(long attributeBlueprintId, AttributeBlueprintDataType dataType,
        [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
        => (await blueprints.ChangeDataTypeAsync(attributeBlueprintId, dataType, ct)).ValueOrThrow(true);

    [Authorize(Policy = "Admin")]
    public async Task<bool> SetAttributeBlueprintStatus(long attributeBlueprintId, bool isActive,
        [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
        => (isActive ? await blueprints.ActivateAsync(attributeBlueprintId, ct)
            : await blueprints.DeactivateAsync(attributeBlueprintId, ct)).ValueOrThrow(true);

    [Authorize(Policy = "Admin")]
    public async Task<bool> SetAttributeBlueprintArchiveStatus(long attributeBlueprintId, bool isArchived,
        [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
        => (isArchived ? await blueprints.ArchiveAsync(attributeBlueprintId, ct)
            : await blueprints.UnarchiveAsync(attributeBlueprintId, ct)).ValueOrThrow(true);

    [Authorize(Policy = "Admin")]
    public async Task<bool> SetAttributeBlueprintScope(SetAttributeBlueprintScopeInput input,
        [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
        => (await blueprints.SetScopeAsync(input.AttributeBlueprintId, input.Scope, input.CategoryId, ct)).ValueOrThrow(true);

    [Authorize(Policy = "Admin")]
    public async Task<bool> SetAttributeBlueprintAliases(SetAttributeBlueprintAliasesInput input,
        [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
        => (await blueprints.SetAliasesAsync(input.AttributeBlueprintId, input.Aliases ?? [], ct)).ValueOrThrow(true);

    [Authorize(Policy = "Admin")]
    public async Task<bool> SetAttributeBlueprintUnit(SetAttributeBlueprintUnitInput input,
        [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
        => (await blueprints.SetUnitAsync(input.AttributeBlueprintId, input.UnitId, ct)).ValueOrThrow(true);

    [Authorize(Policy = "Admin")]
    public async Task<bool> DeleteAttributeBlueprint(long attributeBlueprintId, [Service] IAttributeBlueprintService blueprints, CancellationToken ct)
        => (await blueprints.DeleteAsync(attributeBlueprintId, ct)).ValueOrThrow(true);
}
