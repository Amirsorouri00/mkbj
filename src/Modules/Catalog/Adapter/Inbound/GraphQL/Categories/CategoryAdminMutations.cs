using EwaysVNext.Modules.Catalog.Application.Categories.Commands;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Modules.Catalog.QueryApplication.Categories;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using HotChocolate.Authorization;
using HotChocolate.Types.Relay;
using System.Security.Claims;

namespace EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Categories;

public sealed record CreateCategoryInput(string Name, string Slug, string? Description,
    [property: ID] long? ParentId, int DisplayOrder);
public sealed record UpdateCategoryInput([property: ID] long CategoryId, string Name, string Slug,
    string? Description, int DisplayOrder, CategoryStatus? Status);
public sealed record SetCategoryStatusInput(
    [property: ID] long CategoryId, CategoryStatus Status);
public sealed record MoveCategoryInput([property: ID] long CategoryId, [property: ID] long? ParentId,
    int DisplayOrder);
public sealed record CategoryIdInput([property: ID] long CategoryId);
public sealed record BulkDeactivateCategoriesInput([property: ID] IReadOnlyList<long> CategoryIds);
public sealed record AssignCategoryProductInput([property: ID] long CategoryId, [property: ID] long ProductId);
public sealed record UnassignCategoryProductInput([property: ID] long CategoryId, [property: ID] long ProductId);

[ExtendObjectType(typeof(Mutation))]
#pragma warning disable CA1822 // GraphQL resolver methods are intentionally stateless shells.
public sealed class CategoryAdminMutations
{
    //[Authorize(Policy = "Admin")]
    [ID]
    public async Task<long> CreateCategoryAsync([Service] ICategoryService service,
        CreateCategoryInput input, CancellationToken ct = default)
    {
        var result = await service.CreateAsync(new CreateCategoryCommand(
            input.Name, input.Slug, input.Description, input.ParentId, input.DisplayOrder), ct);
        return ResultGraphQLExtensions.ValueOrThrow(result);
    }

    //[Authorize(Policy = "Admin")]
    public async Task<CategoryAdminDto> UpdateCategoryAsync(
        [Service] ICategoryService service,
        [Service] ICategoryAdminQueries queries,
        UpdateCategoryInput input,
        CancellationToken ct = default) =>
        await ReloadAsync(await service.UpdateAsync(
            new UpdateCategoryCommand(input.CategoryId, input.Name, input.Slug,
                input.Description, input.DisplayOrder, input.Status), ct), queries, ct);

    //[Authorize(Policy = "Admin")]
    public async Task<CategoryAdminDto> SetCategoryStatusAsync(
        [Service] ICategoryService service,
        [Service] ICategoryAdminQueries queries,
        SetCategoryStatusInput input,
        CancellationToken ct = default) =>
        await ReloadAsync(await service.SetStatusAsync(
            new SetCategoryStatusCommand(input.CategoryId, input.Status), ct), queries, ct);

    //[Authorize(Policy = "Admin")]
    public async Task<bool> MoveCategoryAsync([Service] ICategoryService service,
        MoveCategoryInput input, CancellationToken ct = default) => SuccessOrThrow(await service.MoveAsync(
            new MoveCategoryCommand(input.CategoryId, input.ParentId, input.DisplayOrder), ct));

    //[Authorize(Policy = "Admin")]
    public async Task<bool> DeactivateCategoriesAsync([Service] ICategoryService service,
        BulkDeactivateCategoriesInput input, CancellationToken ct = default) =>
        SuccessOrThrow(await service.DeactivateManyAsync(
            new BulkDeactivateCategoriesCommand(input.CategoryIds), ct));

    //[Authorize(Policy = "Admin")]
    public async Task<bool> DeleteCategoryAsync([Service] ICategoryService service,
        CategoryIdInput input, CancellationToken ct = default) => SuccessOrThrow(await service.DeleteAsync(
            new DeleteCategoryCommand(input.CategoryId), ct));

    //[Authorize(Policy = "Admin")]
    public async Task<bool> AssignCategoryProductAsync([Service] ICategoryService service,
        AssignCategoryProductInput input, ClaimsPrincipal principal,
        CancellationToken ct = default) => SuccessOrThrow(await service.AssignProductAsync(
            new AssignCategoryProductCommand(input.CategoryId, input.ProductId,
                TryGetActorUserId(principal)), ct));

    //[Authorize(Policy = "Admin")]
    public async Task<bool> UnassignCategoryProductAsync([Service] ICategoryService service,
        UnassignCategoryProductInput input, CancellationToken ct = default) => SuccessOrThrow(await service.UnassignProductAsync(
            new UnassignCategoryProductCommand(input.CategoryId, input.ProductId), ct));

    private static async Task<CategoryAdminDto> ReloadAsync(
        EwaysVNext.Outcomes.Result<CategoryAdminDto> mutationResult,
        ICategoryAdminQueries queries,
        CancellationToken ct)
    {
        var saved = ResultGraphQLExtensions.ValueOrThrow(mutationResult);
        return ResultGraphQLExtensions.ValueOrThrow(
            await queries.GetByIdAsync(saved.Id, ct));
    }

    private static bool SuccessOrThrow(EwaysVNext.Outcomes.Result result)
    {
        if (result.IsSuccess) return true;
        throw ErrorGraphQLExtensions.ToGraphQLException(result.Error!);
    }

    private static Guid? TryGetActorUserId(ClaimsPrincipal? principal)
    {
        var subject = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal?.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
#pragma warning restore CA1822
