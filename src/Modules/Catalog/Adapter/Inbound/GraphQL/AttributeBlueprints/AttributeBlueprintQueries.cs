using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;
using EwaysVNext.Modules.Catalog.QueryApplication.Abstractions.AttributeBlueprints;
using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Queries.Application.Paginations;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.AttributeBlueprints;

[ExtendObjectType(typeof(Query))]
public sealed class AttributeBlueprintQueries
{
    [Authorize(Policy = "Admin")]
    public Task<PaginatedQuery<AttributeBlueprintSnapshot>> AttributeBlueprints(int page, int pageSize, bool includeDeleted,
        [Service] IAttributeBlueprintQueryService blueprints, AttributeBlueprintFilter? filter = null, CancellationToken ct = default)
        => blueprints.ListAsync(Math.Max(page, 1), Math.Clamp(pageSize, 1, 100), includeDeleted, filter, ct);

    [Authorize(Policy = "Admin")]
    public async Task<AttributeBlueprintSnapshot> AttributeBlueprint([GraphQLType(typeof(NonNullType<IdType>))] long id,
        [Service] IAttributeBlueprintQueryService blueprints, CancellationToken ct)
        => (await blueprints.GetByIdAsync(id, ct)).ValueOrThrow();

    [Authorize(Policy = "Admin")]
    public async Task<AttributeBlueprintUsageSnapshot> AttributeBlueprintUsage([GraphQLType(typeof(NonNullType<IdType>))] long id,
        [Service] IAttributeBlueprintQueryService blueprints, CancellationToken ct)
        => (await blueprints.GetUsageAsync(id, ct)).ValueOrThrow();

    [Authorize(Policy = "Admin")]
    public Task<IReadOnlyList<MeasurementUnitSnapshot>> MeasurementUnits([Service] IMeasurementUnitQueryService units, CancellationToken ct)
        => units.ListAsync(ct);
}
