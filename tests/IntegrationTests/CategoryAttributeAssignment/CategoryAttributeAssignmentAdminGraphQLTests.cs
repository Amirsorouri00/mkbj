using System.Net.Http.Headers;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EwaysVNext.IntegrationTests;

[Collection(PostgresSuite.Name)]
public sealed class CategoryAttributeAssignmentAdminGraphQLTests
{
    private readonly PostgresFixture _fixture;

    public CategoryAttributeAssignmentAdminGraphQLTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AssignCategoryAttribute_Links_An_Attribute_To_A_Category()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var categoryId = NextId();
        var attributeBlueprintId = NextId();

        var snapshot = await AssignAsync(admin, categoryId, attributeBlueprintId, isFilterable: true);

        snapshot.CategoryId.Should().Be(categoryId);
        snapshot.AttributeBlueprintId.Should().Be(attributeBlueprintId);
        snapshot.IsFilterable.Should().BeTrue();
        snapshot.IsRemoved.Should().BeFalse();
    }

    [Fact]
    public async Task AssignCategoryAttribute_Allows_All_Four_Flags_True_At_Once()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var snapshot = await AssignAsync(
            admin, NextId(), NextId(),
            isRequired: true, isFilterable: true, isSearchable: true, isInTitle: true, titleOrder: 1);

        snapshot.IsRequired.Should().BeTrue();
        snapshot.IsFilterable.Should().BeTrue();
        snapshot.IsSearchable.Should().BeTrue();
        snapshot.IsInTitle.Should().BeTrue();
        snapshot.TitleOrder.Should().Be(1);
    }

    [Fact]
    public async Task AssignCategoryAttribute_Rejects_A_Duplicate_Assignment()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var categoryId = NextId();
        var attributeBlueprintId = NextId();
        await AssignAsync(admin, categoryId, attributeBlueprintId);

        var response = await TryAssignAsync(admin, categoryId, attributeBlueprintId);

        (await AdminGraphQLClient.FirstErrorCodeAsync(response)).Should().Be("CATEGORY_ATTRIBUTE_ALREADY_ASSIGNED");
    }

    [Fact]
    public async Task SetCategoryAttributePrimary_Forces_Required_On_And_Locks_It()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var snapshot = await AssignAsync(admin, NextId(), NextId(), isRequired: false);
        var updated = await SetPrimaryAsync(admin, snapshot.Id, true);

        updated.IsPrimary.Should().BeTrue();
        updated.IsRequired.Should().BeTrue();

        var response = await TrySetRequiredAsync(admin, snapshot.Id, false);
        (await AdminGraphQLClient.FirstErrorCodeAsync(response)).Should().Be("CATEGORY_ATTRIBUTE_REQUIRED_LOCKED_BY_PRIMARY");
    }

    [Fact]
    public async Task SetCategoryAttributePrimary_Fails_Once_The_Category_Already_Has_Nine_Primary_Attributes()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var categoryId = NextId();
        for (var i = 0; i < 9; i++)
            await AssignAsync(admin, categoryId, NextId(), isPrimary: true);

        var response = await TryAssignAsync(admin, categoryId, NextId(), isPrimary: true);

        (await AdminGraphQLClient.FirstErrorCodeAsync(response)).Should().Be("CATEGORY_ATTRIBUTE_PRIMARY_LIMIT_REACHED");
    }

    [Fact]
    public async Task SetCategoryAttributePrimary_Reaffirming_An_Existing_Primary_Does_Not_Count_Against_Its_Own_Limit()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var categoryId = NextId();
        long firstAssignmentId = 0;
        for (var i = 0; i < 9; i++)
        {
            var snapshot = await AssignAsync(admin, categoryId, NextId(), isPrimary: true);
            if (i == 0) firstAssignmentId = snapshot.Id;
        }

        var updated = await SetPrimaryAsync(admin, firstAssignmentId, true);

        updated.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task SetCategoryAttributeVariantGenerating_Disables_Primary()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var snapshot = await AssignAsync(admin, NextId(), NextId(), isPrimary: true);

        var updated = await SetVariantGeneratingAsync(admin, snapshot.Id, true);

        updated.IsVariantGenerating.Should().BeTrue();
        updated.IsPrimary.Should().BeFalse();
    }

    [Fact]
    public async Task SetCategoryAttributePrimary_Fails_While_VariantGenerating_Is_Enabled()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var snapshot = await AssignAsync(admin, NextId(), NextId(), isVariantGenerating: true);

        var response = await TrySetPrimaryAsync(admin, snapshot.Id, true);

        (await AdminGraphQLClient.FirstErrorCodeAsync(response)).Should().Be("CATEGORY_ATTRIBUTE_PRIMARY_BLOCKED_BY_VARIANT_GENERATING");
    }

    [Fact]
    public async Task UnassignCategoryAttribute_Marks_It_Removed_And_Excludes_It_From_The_Category_Listing()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var categoryId = NextId();
        var snapshot = await AssignAsync(admin, categoryId, NextId());

        var removed = await UnassignAsync(admin, snapshot.Id);
        removed.IsRemoved.Should().BeTrue();

        var remaining = await ListByCategoryAsync(admin, categoryId);
        remaining.Should().NotContain(a => a.Id == snapshot.Id);
    }

    [Fact]
    public async Task ReorderCategoryAttribute_Requires_The_Attribute_To_Be_InTitle()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var snapshot = await AssignAsync(admin, NextId(), NextId(), isInTitle: false);

        var response = await TryReorderAsync(admin, snapshot.Id, 3);

        (await AdminGraphQLClient.FirstErrorCodeAsync(response)).Should().Be("CATEGORY_ATTRIBUTE_NOT_IN_TITLE");
    }

    [Fact]
    public async Task ReorderCategoryAttribute_Updates_The_Title_Order_For_An_InTitle_Attribute()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var snapshot = await AssignAsync(admin, NextId(), NextId(), isInTitle: true, titleOrder: 1);

        var updated = await ReorderAsync(admin, snapshot.Id, 5);

        updated.TitleOrder.Should().Be(5);
    }

    [Fact]
    public async Task CategoryAttributeAdminRows_Orders_VariantGenerating_Then_Primary_Then_Standard()
    {
        using var factory = _fixture.CreateFactory();
        using var admin = AdminClient(factory);

        var categoryId = NextId();
        var standard = await AssignAsync(admin, categoryId, NextId());
        var primary = await AssignAsync(admin, categoryId, NextId(), isPrimary: true);
        var variantGenerating = await AssignAsync(admin, categoryId, NextId(), isVariantGenerating: true);

        var rows = await AdminRowsAsync(admin, categoryId);

        rows.Select(r => r.Id).Should().ContainInOrder(variantGenerating.Id, primary.Id, standard.Id);
    }

    [Fact]
    public async Task Admin_Graphql_Endpoint_Rejects_A_Caller_Without_The_Admin_Role()
    {
        using var factory = _fixture.CreateFactory();
        using var customer = factory.CreateClient();
        customer.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateCustomerToken());

        var response = await TryAssignAsync(customer, NextId(), NextId());

        (await AdminGraphQLClient.FirstErrorCodeAsync(response)).Should().Be("AUTH_NOT_AUTHORIZED");
    }

    [Fact]
    public async Task Admin_Graphql_Endpoint_Rejects_An_Unauthenticated_Caller()
    {
        using var factory = _fixture.CreateFactory();
        using var anonymous = factory.CreateClient();

        var response = await TryAssignAsync(anonymous, NextId(), NextId());

        (await AdminGraphQLClient.FirstErrorCodeAsync(response)).Should().Be("AUTH_NOT_AUTHENTICATED");
    }

    private static HttpClient AdminClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateAdminToken());
        return client;
    }

    private static long NextId() => Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);

    private const string AssignMutation = """
        mutation($categoryId: Long!, $attributeBlueprintId: Long!, $isRequired: Boolean!, $isFilterable: Boolean!, $isSearchable: Boolean!, $isInTitle: Boolean!, $titleOrder: Int, $isPrimary: Boolean!, $isVariantGenerating: Boolean!) {
          assignCategoryAttribute(categoryId: $categoryId, attributeBlueprintId: $attributeBlueprintId, isRequired: $isRequired, isFilterable: $isFilterable, isSearchable: $isSearchable, isInTitle: $isInTitle, titleOrder: $titleOrder, isPrimary: $isPrimary, isVariantGenerating: $isVariantGenerating) {
            id categoryId attributeBlueprintId isRequired isFilterable isSearchable isInTitle titleOrder isPrimary isVariantGenerating isRemoved
          }
        }
        """;

    private static Task<HttpResponseMessage> TryAssignAsync(
        HttpClient admin, long categoryId, long attributeBlueprintId,
        bool isRequired = false, bool isFilterable = false, bool isSearchable = false,
        bool isInTitle = false, int? titleOrder = null, bool isPrimary = false, bool isVariantGenerating = false) =>
        AdminGraphQLClient.PostAsync(admin, AssignMutation, new
        {
            categoryId,
            attributeBlueprintId,
            isRequired,
            isFilterable,
            isSearchable,
            isInTitle,
            titleOrder,
            isPrimary,
            isVariantGenerating,
        });

    private static async Task<AssignmentShape> AssignAsync(
        HttpClient admin, long categoryId, long attributeBlueprintId,
        bool isRequired = false, bool isFilterable = false, bool isSearchable = false,
        bool isInTitle = false, int? titleOrder = null, bool isPrimary = false, bool isVariantGenerating = false)
    {
        var response = await AdminGraphQLClient.PostForResultAsync<Dictionary<string, AssignmentShape>>(
            admin, AssignMutation, new
            {
                categoryId,
                attributeBlueprintId,
                isRequired,
                isFilterable,
                isSearchable,
                isInTitle,
                titleOrder,
                isPrimary,
                isVariantGenerating,
            });
        response.Errors.Should().BeNullOrEmpty();
        return response.Data!["assignCategoryAttribute"];
    }

    private const string UnassignMutation = """
        mutation($assignmentId: Long!) {
          unassignCategoryAttribute(assignmentId: $assignmentId) {
            id categoryId attributeBlueprintId isRequired isFilterable isSearchable isInTitle titleOrder isPrimary isVariantGenerating isRemoved
          }
        }
        """;

    private static async Task<AssignmentShape> UnassignAsync(HttpClient admin, long assignmentId)
    {
        var response = await AdminGraphQLClient.PostForResultAsync<Dictionary<string, AssignmentShape>>(
            admin, UnassignMutation, new { assignmentId });
        response.Errors.Should().BeNullOrEmpty();
        return response.Data!["unassignCategoryAttribute"];
    }

    private const string SetRequiredMutation = """
        mutation($assignmentId: Long!, $isRequired: Boolean!) {
          setCategoryAttributeRequired(assignmentId: $assignmentId, isRequired: $isRequired) {
            id categoryId attributeBlueprintId isRequired isFilterable isSearchable isInTitle titleOrder isPrimary isVariantGenerating isRemoved
          }
        }
        """;

    private static Task<HttpResponseMessage> TrySetRequiredAsync(HttpClient admin, long assignmentId, bool isRequired) =>
        AdminGraphQLClient.PostAsync(admin, SetRequiredMutation, new { assignmentId, isRequired });

    private const string SetPrimaryMutation = """
        mutation($assignmentId: Long!, $isPrimary: Boolean!) {
          setCategoryAttributePrimary(assignmentId: $assignmentId, isPrimary: $isPrimary) {
            id categoryId attributeBlueprintId isRequired isFilterable isSearchable isInTitle titleOrder isPrimary isVariantGenerating isRemoved
          }
        }
        """;

    private static async Task<AssignmentShape> SetPrimaryAsync(HttpClient admin, long assignmentId, bool isPrimary)
    {
        var response = await AdminGraphQLClient.PostForResultAsync<Dictionary<string, AssignmentShape>>(
            admin, SetPrimaryMutation, new { assignmentId, isPrimary });
        response.Errors.Should().BeNullOrEmpty();
        return response.Data!["setCategoryAttributePrimary"];
    }

    private static Task<HttpResponseMessage> TrySetPrimaryAsync(HttpClient admin, long assignmentId, bool isPrimary) =>
        AdminGraphQLClient.PostAsync(admin, SetPrimaryMutation, new { assignmentId, isPrimary });

    private const string SetVariantGeneratingMutation = """
        mutation($assignmentId: Long!, $isVariantGenerating: Boolean!) {
          setCategoryAttributeVariantGenerating(assignmentId: $assignmentId, isVariantGenerating: $isVariantGenerating) {
            id categoryId attributeBlueprintId isRequired isFilterable isSearchable isInTitle titleOrder isPrimary isVariantGenerating isRemoved
          }
        }
        """;

    private static async Task<AssignmentShape> SetVariantGeneratingAsync(HttpClient admin, long assignmentId, bool isVariantGenerating)
    {
        var response = await AdminGraphQLClient.PostForResultAsync<Dictionary<string, AssignmentShape>>(
            admin, SetVariantGeneratingMutation, new { assignmentId, isVariantGenerating });
        response.Errors.Should().BeNullOrEmpty();
        return response.Data!["setCategoryAttributeVariantGenerating"];
    }

    private const string ReorderMutation = """
        mutation($assignmentId: Long!, $titleOrder: Int!) {
          reorderCategoryAttribute(assignmentId: $assignmentId, titleOrder: $titleOrder) {
            id categoryId attributeBlueprintId isRequired isFilterable isSearchable isInTitle titleOrder isPrimary isVariantGenerating isRemoved
          }
        }
        """;

    private static async Task<AssignmentShape> ReorderAsync(HttpClient admin, long assignmentId, int titleOrder)
    {
        var response = await AdminGraphQLClient.PostForResultAsync<Dictionary<string, AssignmentShape>>(
            admin, ReorderMutation, new { assignmentId, titleOrder });
        response.Errors.Should().BeNullOrEmpty();
        return response.Data!["reorderCategoryAttribute"];
    }

    private static Task<HttpResponseMessage> TryReorderAsync(HttpClient admin, long assignmentId, int titleOrder) =>
        AdminGraphQLClient.PostAsync(admin, ReorderMutation, new { assignmentId, titleOrder });

    private const string ListByCategoryQuery = """
        query($categoryId: Long!) {
          categoryAttributeAssignments(categoryId: $categoryId) {
            id categoryId attributeBlueprintId isRequired isFilterable isSearchable isInTitle titleOrder isPrimary isVariantGenerating isRemoved
          }
        }
        """;

    private static async Task<IReadOnlyList<AssignmentShape>> ListByCategoryAsync(HttpClient admin, long categoryId)
    {
        var response = await AdminGraphQLClient.PostForResultAsync<Dictionary<string, AssignmentShape[]>>(
            admin, ListByCategoryQuery, new { categoryId });
        response.Errors.Should().BeNullOrEmpty();
        return response.Data!["categoryAttributeAssignments"];
    }

    private const string AdminRowsQuery = """
        query($categoryId: Long!) {
          categoryAttributeAdminRows(categoryId: $categoryId) {
            id categoryId attributeBlueprintId isRequired isFilterable isSearchable isInTitle titleOrder isPrimary isVariantGenerating priorityGroup
          }
        }
        """;

    private static async Task<IReadOnlyList<AdminRowShape>> AdminRowsAsync(HttpClient admin, long categoryId)
    {
        var response = await AdminGraphQLClient.PostForResultAsync<Dictionary<string, AdminRowShape[]>>(
            admin, AdminRowsQuery, new { categoryId });
        response.Errors.Should().BeNullOrEmpty();
        return response.Data!["categoryAttributeAdminRows"];
    }

    private sealed record AssignmentShape(
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

    private sealed record AdminRowShape(
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
        string PriorityGroup);
}
