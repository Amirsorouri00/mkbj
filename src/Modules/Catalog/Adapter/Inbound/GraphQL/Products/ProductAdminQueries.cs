using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.QueryApplication.Products;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Security;
using EwaysVNext.Queries.Application.Paginations;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Products;

// Both attributes on every resolver, never on the class: a type-level attribute on an
// [ExtendObjectType] is merged onto the whole Query and would gate every other module's fields
// with the catalogue's policy.

[ExtendObjectType(typeof(Query))]
public sealed class ProductAdminQueries
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public Task<PaginatedQuery<ProductAdminRowDto>> AdminProductsAsync(
        string? q, ProductStatus? status,
        [GraphQLType(typeof(IdType))] long? brandId,
        [GraphQLType(typeof(IdType))] long? categoryId,
        int page, int pageSize,
        [Service] ICatalogAdminQueries queries, CancellationToken ct = default) =>
        queries.ListProductsAsync(new ProductAdminFilter(q, status, brandId, categoryId), page, pageSize, ct);

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<ProductAdminDetailDto> AdminProductAsync(
        [GraphQLType(typeof(NonNullType<IdType>))] long id,
        [Service] ICatalogAdminQueries queries, CancellationToken ct = default) =>
        ResultGraphQLExtensions.ValueOrThrow(await queries.GetProductAsync(id, ct));
}
