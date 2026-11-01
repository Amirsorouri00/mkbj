using System.Security.Claims;

using AuthorizationService.Abstractions.Attributes;
using AuthorizationService.Abstractions.Policies;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.QueryApplication.Products;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Products;

public sealed record CreateProductInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long BrandId,
    [property: GraphQLType(typeof(NonNullType<IdType>))] long PrimaryCategoryId,
    string Title,
    string? Slug,
    string? ShortDescription,
    string? Description);

public sealed record UpdateProductInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long ProductId,
    [property: GraphQLType(typeof(NonNullType<IdType>))] long BrandId,
    [property: GraphQLType(typeof(NonNullType<IdType>))] long PrimaryCategoryId,
    string Title,
    string? ShortDescription,
    string? Description);

public sealed record SetProductSecondaryCategoriesInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long ProductId,
    [property: GraphQLType(typeof(ListType<NonNullType<IdType>>))] IReadOnlyList<long> CategoryIds);

public sealed record AddVariantInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long ProductId,
    string? Sku,
    IReadOnlyDictionary<string, string>? Attributes);

public sealed record SetVariantActiveInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long ProductId,
    [property: GraphQLType(typeof(NonNullType<IdType>))] long VariantId,
    bool Active);

public sealed record AddProductIdentifierInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long ProductId,
    ProductIdentifierType Type,
    string Value,
    string? SourceDomain);

public sealed record RemoveProductIdentifierInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long ProductId,
    [property: GraphQLType(typeof(NonNullType<IdType>))] long IdentifierId);

public sealed record SetProductActiveInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long ProductId,
    bool Active);

public sealed record UploadProductMediaInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long ProductId,
    IFile File,
    string? Alt,
    MediaUsageRights? Rights,
    int? SortOrder);

public sealed record RemoveProductMediaInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long ProductId,
    [property: GraphQLType(typeof(NonNullType<IdType>))] long MediaId);

public sealed record BulkEditProductsInput(
    [property: GraphQLType(typeof(ListType<NonNullType<IdType>>))] IReadOnlyList<long> ProductIds,
    BulkStatusChange Status,
    [property: GraphQLType(typeof(IdType))] long? PrimaryCategoryId,
    [property: GraphQLType(typeof(IdType))] long? BrandId);

public sealed record MergeDuplicateProductInput(
    [property: GraphQLType(typeof(NonNullType<IdType>))] long DuplicateProductId,
    [property: GraphQLType(typeof(NonNullType<IdType>))] long SurvivorProductId);

// Both attributes on every resolver, never on the class — see ProductAdminQueries.

[ExtendObjectType(typeof(Mutation))]
public sealed class ProductAdminMutations
{
    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<ProductAdminDetailDto> CreateProductAsync(
        CreateProductInput input, ClaimsPrincipal user,
        [Service] ICatalogAdminService service, [Service] ICatalogAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var created = await service.CreateProductAsync(
            new CreateProductCommand(
                input.BrandId, input.PrimaryCategoryId, input.Title, input.Slug,
                input.ShortDescription, input.Description),
            Actor(user), ct);

        return await ReloadAsync(created, queries, ct);
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<ProductAdminDetailDto> UpdateProductAsync(
        UpdateProductInput input, ClaimsPrincipal user,
        [Service] ICatalogAdminService service, [Service] ICatalogAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return await ReloadAsync(
            await service.UpdateProductAsync(
                new UpdateProductCommand(
                    input.ProductId, input.BrandId, input.PrimaryCategoryId, input.Title,
                    input.ShortDescription, input.Description),
                Actor(user), ct),
            input.ProductId, queries, ct);
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<ProductAdminDetailDto> SetProductSecondaryCategoriesAsync(
        SetProductSecondaryCategoriesInput input, ClaimsPrincipal user,
        [Service] ICatalogAdminService service, [Service] ICatalogAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return await ReloadAsync(
            await service.SetSecondaryCategoriesAsync(
                input.ProductId, input.CategoryIds ?? [], Actor(user), ct),
            input.ProductId, queries, ct);
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<ProductAdminDetailDto> AddVariantAsync(
        AddVariantInput input, ClaimsPrincipal user,
        [Service] ICatalogAdminService service, [Service] ICatalogAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var added = await service.AddVariantAsync(
            new AddVariantCommand(
                input.ProductId, input.Sku, input.Attributes ?? new Dictionary<string, string>()),
            Actor(user), ct);

        return await ReloadAsync(added.IsSuccess ? Result.Success() : Result.Failure(added.Error!),
            input.ProductId, queries, ct);
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<ProductAdminDetailDto> SetVariantActiveAsync(
        SetVariantActiveInput input, ClaimsPrincipal user,
        [Service] ICatalogAdminService service, [Service] ICatalogAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return await ReloadAsync(
            await service.SetVariantActiveAsync(
                input.ProductId, input.VariantId, input.Active, Actor(user), ct),
            input.ProductId, queries, ct);
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<ProductAdminDetailDto> AddProductIdentifierAsync(
        AddProductIdentifierInput input, ClaimsPrincipal user,
        [Service] ICatalogAdminService service, [Service] ICatalogAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return await ReloadAsync(
            await service.AddIdentifierAsync(
                input.ProductId, new IdentifierInput(input.Type, input.Value, input.SourceDomain),
                Actor(user), ct),
            input.ProductId, queries, ct);
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<ProductAdminDetailDto> RemoveProductIdentifierAsync(
        RemoveProductIdentifierInput input, ClaimsPrincipal user,
        [Service] ICatalogAdminService service, [Service] ICatalogAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return await ReloadAsync(
            await service.RemoveIdentifierAsync(input.ProductId, input.IdentifierId, Actor(user), ct),
            input.ProductId, queries, ct);
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<ProductAdminDetailDto> SetProductActiveAsync(
        SetProductActiveInput input, ClaimsPrincipal user,
        [Service] ICatalogAdminService service, [Service] ICatalogAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return await ReloadAsync(
            await service.SetProductActiveAsync(input.ProductId, input.Active, Actor(user), ct),
            input.ProductId, queries, ct);
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<ProductAdminDetailDto> UploadProductMediaAsync(
        UploadProductMediaInput input, ClaimsPrincipal user,
        [Service] ICatalogAdminService service, [Service] ICatalogAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await using var content = input.File.OpenReadStream();
        var uploaded = await service.UploadMediaAsync(
            new UploadMediaCommand(
                input.ProductId, content, input.File.Length ?? 0, input.Alt,
                // Absent means unknown, not allowed: an unconfirmed image must not be able to
                // make a product sellable.
                input.Rights ?? MediaUsageRights.RightsUnknown, input.SortOrder),
            Actor(user), ct);

        return await ReloadAsync(
            uploaded.IsSuccess ? Result.Success() : Result.Failure(uploaded.Error!),
            input.ProductId, queries, ct);
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<ProductAdminDetailDto> RemoveProductMediaAsync(
        RemoveProductMediaInput input, ClaimsPrincipal user,
        [Service] ICatalogAdminService service, [Service] ICatalogAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return await ReloadAsync(
            await service.RemoveMediaAsync(input.ProductId, input.MediaId, Actor(user), ct),
            input.ProductId, queries, ct);
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<BulkEditResult> BulkEditProductsAsync(
        BulkEditProductsInput input, ClaimsPrincipal user,
        [Service] ICatalogAdminService service, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Returns the per-item outcomes rather than one product: the whole point of a bulk edit
        // is that some items fail and the caller has to see which.
        return ResultGraphQLExtensions.ValueOrThrow(
            await service.BulkEditAsync(
                new BulkProductEdit(
                    input.ProductIds ?? [], input.Status, input.PrimaryCategoryId, input.BrandId),
                Actor(user), ct));
    }

    [Authorize(Policy = AuthPolicies.AdminStaff)]
    [RequirePermission(AdminPermissions.CatalogManage)]
    public async Task<ProductAdminDetailDto> MergeDuplicateProductAsync(
        MergeDuplicateProductInput input, ClaimsPrincipal user,
        [Service] ICatalogAdminService service, [Service] ICatalogAdminQueries queries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // The duplicate is reloaded, not the survivor: it is the one that changed, and its
        // mergedIntoProductId is what the caller needs to see.
        return await ReloadAsync(
            await service.MergeDuplicateAsync(
                input.DuplicateProductId, input.SurvivorProductId, Actor(user), ct),
            input.DuplicateProductId, queries, ct);
    }

    private static async Task<ProductAdminDetailDto> ReloadAsync(
        EwaysVNext.Outcomes.Result<long> created, ICatalogAdminQueries queries, CancellationToken ct)
    {
        var productId = ResultGraphQLExtensions.ValueOrThrow(created);
        return ResultGraphQLExtensions.ValueOrThrow(await queries.GetProductAsync(productId, ct));
    }

    /// <summary>
    /// Every mutation answers with the product as it now is, read back through the same
    /// projection the queries use — so a client never has to guess what its write produced.
    /// </summary>
    private static async Task<ProductAdminDetailDto> ReloadAsync(
        Result outcome, long productId, ICatalogAdminQueries queries, CancellationToken ct)
    {
        outcome.ThrowIfFailure();
        return ResultGraphQLExtensions.ValueOrThrow(await queries.GetProductAsync(productId, ct));
    }

    private static AdminActor Actor(ClaimsPrincipal user) =>
        AdminActor.TryFrom(user, out var actor)
            ? actor!
            : throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("The token passed the admin policy but carries no usable subject.")
                .SetCode("UNAUTHORIZED")
                .Build());
}
