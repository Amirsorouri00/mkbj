using System.Net.Http.Headers;
using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests;

[Collection(LocalDbSuite.Name)]
public sealed class CategoryAttributeExclusionGraphQLTests
{
    private readonly LocalDbFixture _fixture;

    public CategoryAttributeExclusionGraphQLTests(LocalDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ExcludeInheritedCategoryAttribute_Excludes_A_Grandparent_Attribute_From_The_Category_Listing()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var (grandparentId, parentId, categoryId) = await SeedThreeLevelHierarchyAsync(factory);
        var grandparentAssignment = await AssignAsync(admin, grandparentId, NextId());

        var excluded = await ExcludeAsync(admin, categoryId, grandparentAssignment.Id);

        excluded.CategoryId.Should().Be(categoryId);
        excluded.SourceAssignmentId.Should().Be(grandparentAssignment.Id);
        excluded.IsExcluded.Should().BeTrue();
    }

    [Fact]
    public async Task ExcludeInheritedCategoryAttribute_Rejects_An_Assignment_That_Is_Not_A_Real_Ancestor()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var (_, _, categoryId) = await SeedThreeLevelHierarchyAsync(factory);
        var unrelatedAssignment = await AssignAsync(admin, NextId(), NextId());

        var response = await TryExcludeAsync(admin, categoryId, unrelatedAssignment.Id);

        (await AdminGraphQLClient.FirstErrorCodeAsync(response)).Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_NOT_AN_ANCESTOR");
    }

    [Fact]
    public async Task ExcludeInheritedCategoryAttribute_Rejects_Its_Own_Category_Assignment()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var (_, _, categoryId) = await SeedThreeLevelHierarchyAsync(factory);
        var ownAssignment = await AssignAsync(admin, categoryId, NextId());

        var response = await TryExcludeAsync(admin, categoryId, ownAssignment.Id);

        (await AdminGraphQLClient.FirstErrorCodeAsync(response)).Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_NOT_INHERITED");
    }

    [Fact]
    public async Task ExcludeInheritedCategoryAttribute_Prefers_The_Nearest_Ancestor_When_Both_Assign_The_Same_Attribute()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var (grandparentId, parentId, categoryId) = await SeedThreeLevelHierarchyAsync(factory);
        var attributeBlueprintId = NextId();
        var grandparentAssignment = await AssignAsync(admin, grandparentId, attributeBlueprintId);
        var parentAssignment = await AssignAsync(admin, parentId, attributeBlueprintId);

        var shadowedResponse = await TryExcludeAsync(admin, categoryId, grandparentAssignment.Id);
        (await AdminGraphQLClient.FirstErrorCodeAsync(shadowedResponse)).Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_SHADOWED");

        var excluded = await ExcludeAsync(admin, categoryId, parentAssignment.Id);
        excluded.SourceAssignmentId.Should().Be(parentAssignment.Id);
    }

    [Fact]
    public async Task IncludeInheritedCategoryAttribute_Reverses_A_Previous_Exclusion()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var (grandparentId, _, categoryId) = await SeedThreeLevelHierarchyAsync(factory);
        var grandparentAssignment = await AssignAsync(admin, grandparentId, NextId());
        await ExcludeAsync(admin, categoryId, grandparentAssignment.Id);

        var included = await IncludeAsync(admin, categoryId, grandparentAssignment.Id);

        included.IsExcluded.Should().BeFalse();
    }

    [Fact]
    public async Task IncludeInheritedCategoryAttribute_Fails_When_Nothing_Was_Excluded()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var response = await TryIncludeAsync(admin, NextId(), NextId());

        (await AdminGraphQLClient.FirstErrorCodeAsync(response)).Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_NOT_FOUND");
    }

    [Fact]
    public async Task SetCategoryAttributeExclusions_Applies_A_Mixed_Batch_Of_Additions_And_ReInclusions_Atomically()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var (grandparentId, _, categoryId) = await SeedThreeLevelHierarchyAsync(factory);
        var assignmentA = await AssignAsync(admin, grandparentId, NextId());
        var assignmentB = await AssignAsync(admin, grandparentId, NextId());
        await ExcludeAsync(admin, categoryId, assignmentA.Id);

        var excluded = await SetExclusionsAsync(admin, categoryId, new[] { assignmentB.Id });

        excluded.Should().ContainSingle(e => e.SourceAssignmentId == assignmentB.Id && e.IsExcluded);

        var reincludeResponse = await TryIncludeAsync(admin, categoryId, assignmentA.Id);
        (await AdminGraphQLClient.FirstErrorCodeAsync(reincludeResponse)).Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_NOT_FOUND");
    }

    [Fact]
    public async Task SetCategoryAttributeExclusions_Is_AllOrNothing_When_One_Item_Is_Shadowed()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var (grandparentId, parentId, categoryId) = await SeedThreeLevelHierarchyAsync(factory);
        var attributeBlueprintId = NextId();
        var grandparentAssignment = await AssignAsync(admin, grandparentId, attributeBlueprintId);
        await AssignAsync(admin, parentId, attributeBlueprintId);

        var response = await TrySetExclusionsAsync(admin, categoryId, new[] { grandparentAssignment.Id });

        (await AdminGraphQLClient.FirstErrorCodeAsync(response)).Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_SHADOWED");
    }

    [Fact]
    public async Task EffectiveCategoryAttributes_Reports_Direct_And_Inherited_Attributes_With_Exclusion_State()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var (grandparentId, _, categoryId) = await SeedThreeLevelHierarchyAsync(factory);
        var directAssignment = await AssignAsync(admin, categoryId, NextId());
        var inheritedAssignment = await AssignAsync(admin, grandparentId, NextId());
        await ExcludeAsync(admin, categoryId, inheritedAssignment.Id);

        var effective = await GetEffectiveAttributesAsync(admin, categoryId);

        effective.Should().ContainSingle(a =>
            a.AssignmentId == directAssignment.Id && !a.IsInherited && !a.IsExcluded);
        effective.Should().ContainSingle(a =>
            a.AssignmentId == inheritedAssignment.Id && a.IsInherited && a.IsExcluded);
    }

    [Fact]
    public async Task EffectiveCategoryAttributes_Only_Reports_The_Nearest_Ancestor_When_Both_Assign_The_Same_Attribute()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var (grandparentId, parentId, categoryId) = await SeedThreeLevelHierarchyAsync(factory);
        var attributeBlueprintId = NextId();
        await AssignAsync(admin, grandparentId, attributeBlueprintId);
        var parentAssignment = await AssignAsync(admin, parentId, attributeBlueprintId);

        var effective = await GetEffectiveAttributesAsync(admin, categoryId);

        effective.Should().ContainSingle(a => a.AttributeBlueprintId == attributeBlueprintId);
        effective.Single(a => a.AttributeBlueprintId == attributeBlueprintId).AssignmentId.Should().Be(parentAssignment.Id);
    }

    private static async Task<(long GrandparentId, long ParentId, long CategoryId)> SeedThreeLevelHierarchyAsync(
        WebApplicationFactory<Program> factory)
    {
        var grandparentId = NextId();
        var parentId = NextId();
        var categoryId = NextId();
        var now = DateTimeOffset.UtcNow;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        db.Categories.Add(Category.Create(grandparentId, $"Grandparent {grandparentId}", $"grandparent-{grandparentId}", null, null, 0, now));
        db.Categories.Add(Category.Create(parentId, $"Parent {parentId}", $"parent-{parentId}", null, grandparentId, 0, now));
        db.Categories.Add(Category.Create(categoryId, $"Category {categoryId}", $"category-{categoryId}", null, parentId, 0, now));
        await db.SaveChangesAsync();

        return (grandparentId, parentId, categoryId);
    }

    private static HttpClient AdminClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateAdminToken());
        return client;
    }

    private static long NextId() => Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);

    private const string AssignMutation = """
        mutation($categoryId: Long!, $attributeBlueprintId: Long!) {
          assignCategoryAttribute(categoryId: $categoryId, attributeBlueprintId: $attributeBlueprintId, isRequired: false, isFilterable: false, isSearchable: false, isInTitle: false, titleOrder: null, isPrimary: false, isVariantGenerating: false) {
            id categoryId attributeBlueprintId
          }
        }
        """;

    private static async Task<AssignmentShape> AssignAsync(HttpClient admin, long categoryId, long attributeBlueprintId)
    {
        var response = await AdminGraphQLClient.PostForResultAsync<Dictionary<string, AssignmentShape>>(
            admin, AssignMutation, new { categoryId, attributeBlueprintId });
        response.Errors.Should().BeNullOrEmpty();
        return response.Data!["assignCategoryAttribute"];
    }

    private const string ExcludeMutation = """
        mutation($categoryId: Long!, $sourceAssignmentId: Long!) {
          excludeInheritedCategoryAttribute(categoryId: $categoryId, sourceAssignmentId: $sourceAssignmentId) {
            categoryId sourceAssignmentId isExcluded
          }
        }
        """;

    private static Task<HttpResponseMessage> TryExcludeAsync(HttpClient admin, long categoryId, long sourceAssignmentId) =>
        AdminGraphQLClient.PostAsync(admin, ExcludeMutation, new { categoryId, sourceAssignmentId });

    private static async Task<ExclusionShape> ExcludeAsync(HttpClient admin, long categoryId, long sourceAssignmentId)
    {
        var response = await AdminGraphQLClient.PostForResultAsync<Dictionary<string, ExclusionShape>>(
            admin, ExcludeMutation, new { categoryId, sourceAssignmentId });
        response.Errors.Should().BeNullOrEmpty();
        return response.Data!["excludeInheritedCategoryAttribute"];
    }

    private const string IncludeMutation = """
        mutation($categoryId: Long!, $sourceAssignmentId: Long!) {
          includeInheritedCategoryAttribute(categoryId: $categoryId, sourceAssignmentId: $sourceAssignmentId) {
            categoryId sourceAssignmentId isExcluded
          }
        }
        """;

    private static Task<HttpResponseMessage> TryIncludeAsync(HttpClient admin, long categoryId, long sourceAssignmentId) =>
        AdminGraphQLClient.PostAsync(admin, IncludeMutation, new { categoryId, sourceAssignmentId });

    private static async Task<ExclusionShape> IncludeAsync(HttpClient admin, long categoryId, long sourceAssignmentId)
    {
        var response = await AdminGraphQLClient.PostForResultAsync<Dictionary<string, ExclusionShape>>(
            admin, IncludeMutation, new { categoryId, sourceAssignmentId });
        response.Errors.Should().BeNullOrEmpty();
        return response.Data!["includeInheritedCategoryAttribute"];
    }

    private const string SetExclusionsMutation = """
        mutation($categoryId: Long!, $excludedSourceAssignmentIds: [Long!]!) {
          setCategoryAttributeExclusions(categoryId: $categoryId, excludedSourceAssignmentIds: $excludedSourceAssignmentIds) {
            categoryId sourceAssignmentId isExcluded
          }
        }
        """;

    private static Task<HttpResponseMessage> TrySetExclusionsAsync(HttpClient admin, long categoryId, IReadOnlyList<long> excludedSourceAssignmentIds) =>
        AdminGraphQLClient.PostAsync(admin, SetExclusionsMutation, new { categoryId, excludedSourceAssignmentIds });

    private static async Task<IReadOnlyList<ExclusionShape>> SetExclusionsAsync(HttpClient admin, long categoryId, IReadOnlyList<long> excludedSourceAssignmentIds)
    {
        var response = await AdminGraphQLClient.PostForResultAsync<Dictionary<string, ExclusionShape[]>>(
            admin, SetExclusionsMutation, new { categoryId, excludedSourceAssignmentIds });
        response.Errors.Should().BeNullOrEmpty();
        return response.Data!["setCategoryAttributeExclusions"];
    }

    private sealed record AssignmentShape(long Id, long CategoryId, long AttributeBlueprintId);

    private sealed record ExclusionShape(long CategoryId, long SourceAssignmentId, bool IsExcluded);

    private const string EffectiveAttributesQuery = """
        query($categoryId: Long!) {
          effectiveCategoryAttributes(categoryId: $categoryId) {
            assignmentId sourceCategoryId attributeBlueprintId isInherited isExcluded
          }
        }
        """;

    private static async Task<IReadOnlyList<EffectiveAttributeShape>> GetEffectiveAttributesAsync(HttpClient admin, long categoryId)
    {
        var response = await AdminGraphQLClient.PostForResultAsync<Dictionary<string, EffectiveAttributeShape[]>>(
            admin, EffectiveAttributesQuery, new { categoryId });
        response.Errors.Should().BeNullOrEmpty();
        return response.Data!["effectiveCategoryAttributes"];
    }

    private sealed record EffectiveAttributeShape(
        long AssignmentId, long SourceCategoryId, long AttributeBlueprintId, bool IsInherited, bool IsExcluded);
}
