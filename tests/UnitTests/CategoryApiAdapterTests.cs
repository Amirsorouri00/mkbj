using AwesomeAssertions;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Categories;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Categories;
using EwaysVNext.Modules.Catalog.Application.Categories.Commands;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Modules.Catalog.QueryApplication.Categories;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Text.Json;
using Xunit;
using CategoryAdminQueries = EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.Categories.CategoryAdminQueries;
using FrameworkError = EwaysVNext.Outcomes.Error;
using HotChocolateAuthorizeAttribute = HotChocolate.Authorization.AuthorizeAttribute;

namespace EwaysVNext.UnitTests;

public sealed class CategoryApiAdapterTests
{
    private static readonly long ProductId = 1;

    [Fact]
    public async Task Category_API_uses_the_canonical_GraphQl_server_and_REST_routes()
    {
        await using var fixture = await ApiFixture.CreateAsync();

        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin")], "test"));
        var result = await executor.ExecuteAsync(OperationRequestBuilder.New()
            .SetDocument("{ categoryHierarchy { id name } }")
            .SetUser(admin)
            .Build());

        result.ToJson().Should().NotContain("errors");
        fixture.Endpoints.Should().Contain(endpoint =>
            endpoint.RoutePattern.RawText!.StartsWith("/graphql", StringComparison.Ordinal));
        fixture.Endpoints.Should().NotContain(endpoint => endpoint.RoutePattern.RawText!
            .StartsWith("/api/v1/admin/category/graphql", StringComparison.Ordinal));
        fixture.Endpoints.Select(endpoint => endpoint.RoutePattern.RawText).Should().Contain([
            "/api/v1/categories/tree",
            "/api/v1/categories/{id:long}",
            "/api/v1/categories/{id:long}/products",
            "/api/v1/products/{productId:long}/categories"
        ]);
    }

    [Fact(Skip = "TODO(Identity): restore authorization assertions when the Identity system is complete.")]
    public async Task Admin_GraphQl_registers_the_shared_schema_with_authorized_fields_and_endpoint_guards()
    {
        await using var fixture = await ApiFixture.CreateAsync();

        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        const string query = "{ categories(first: 20) { totalCount nodes { id status } } }";
        (await executor.ExecuteAsync(query)).ToJson().Should().Contain("\"errors\"");
        var customer = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Customer")], "test"));
        (await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument(query).SetUser(customer).Build()))
            .ToJson().Should().NotContain("\"categories\": {");
        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test"));
        (await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument(query).SetUser(admin).Build()))
            .ToJson().Should().Contain("\"categories\": {");

        typeof(CategoryAdminQueries).GetMethods()
            .Where(method => method.DeclaringType == typeof(CategoryAdminQueries))
            .Should().OnlyContain(method => method.GetCustomAttributes(typeof(HotChocolateAuthorizeAttribute), true)
                .Cast<HotChocolateAuthorizeAttribute>().Any(attribute => attribute.Policy == "Admin"));
        typeof(CategoryAdminMutations).GetMethods()
            .Where(method => method.DeclaringType == typeof(CategoryAdminMutations))
            .Should().OnlyContain(method => method.GetCustomAttributes(typeof(HotChocolateAuthorizeAttribute), true)
                .Cast<HotChocolateAuthorizeAttribute>().Any(attribute => attribute.Policy == "Admin"));

        var endpoint = fixture.Endpoints.Single(candidate =>
            candidate.RoutePattern.RawText!.StartsWith(
                "/api/v1/admin/graphql", StringComparison.Ordinal)
            && HasAdminPolicy(candidate));
        endpoint.Metadata.GetMetadata<IAuthorizeData>()!.Policy.Should().Be("Admin");
        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("admin");
    }

    [Fact]
    public async Task Admin_categories_use_the_standard_Hot_Chocolate_connection()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin")], "test"));

        var result = await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument("""
                {
                  categories(first: 1) {
                    totalCount
                    nodes { id name }
                    pageInfo { hasNextPage hasPreviousPage startCursor endCursor }
                  }
                }
                """).SetUser(admin).Build());

        var json = result.ToJson();
        json.Should().NotContain("errors");
        using var document = JsonDocument.Parse(json);
        var connection = document.RootElement.GetProperty("data").GetProperty("categories");
        connection.GetProperty("totalCount").GetInt32().Should().Be(2);
        connection.GetProperty("nodes").GetArrayLength().Should().Be(1,
            "first: 1 must let Hot Chocolate select only one node");
        connection.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Admin_categories_expose_parent_summary_and_direct_child_count()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        var executor = await fixture.App.Services.GetRequestExecutorAsync();

        var result = await executor.ExecuteAsync("""
            {
              categories(first: 1) {
                nodes {
                  parent { id name }
                  childCount
                }
              }
            }
            """);

        var json = result.ToJson();
        json.Should().NotContain("errors");
        using var document = JsonDocument.Parse(json);
        var category = document.RootElement.GetProperty("data").GetProperty("categories")
            .GetProperty("nodes").EnumerateArray().Single();
        category.GetProperty("parent").GetProperty("id").GetString().Should().Be("2");
        category.GetProperty("parent").GetProperty("name").GetString().Should().Be("Parent");
        category.GetProperty("childCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Admin_category_products_use_the_standard_Hot_Chocolate_connection()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin")], "test"));

        var result = await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument("""
                {
                  categoryProducts(categoryId: 7, first: 1) {
                    totalCount
                    nodes
                    pageInfo { hasNextPage hasPreviousPage startCursor endCursor }
                  }
                }
                """).SetUser(admin).Build());

        var json = result.ToJson();
        json.Should().NotContain("errors");
        using var document = JsonDocument.Parse(json);
        var connection = document.RootElement.GetProperty("data").GetProperty("categoryProducts");
        connection.GetProperty("totalCount").GetInt32().Should().Be(2);
        connection.GetProperty("nodes").EnumerateArray().Single().GetString().Should().Be("10");
        connection.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Category_connections_expose_all_native_cursor_paging_arguments()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var fields = executor.Schema.QueryType.Fields;

        AssertNativeCursorArguments("categories");
        AssertNativeCursorArguments("categoryProducts");

        void AssertNativeCursorArguments(string fieldName)
        {
            var arguments = fields.Single(field => field.Name == fieldName).Arguments
                .Select(argument => argument.Name)
                .ToArray();

            arguments.Should().Contain("first");
            arguments.Should().Contain("after");
            arguments.Should().Contain("last");
            arguments.Should().Contain("before");
        }
    }

    [Fact]
    public async Task Admin_category_products_preserve_the_not_found_error_with_native_paging()
    {
        var queries = new FakeAdminQueries { ProductCategoryExists = false };
        await using var fixture = await ApiFixture.CreateAsync(adminQueries: queries);
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin")], "test"));

        var result = await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument("""
                {
                  categoryProducts(categoryId: 999, first: 1) {
                    nodes
                  }
                }
                """).SetUser(admin).Build());

        var json = result.ToJson();
        json.Should().Contain("\"code\": \"CATEGORY_NOT_FOUND\"");
        json.Should().NotContain("\"categoryProducts\": {");
    }

    [Fact]
    public async Task Admin_GraphQl_exposes_the_complete_lifecycle_hierarchy_to_admins()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        const string query = """
            {
              categoryHierarchy {
                id name status
                children {
                  id name status
                  children { id name status }
                }
              }
            }
            """;
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin")], "test"));

        // TODO(Identity): restore anonymous-access assertions when GraphQL authorization is enabled.

        var result = await executor.ExecuteAsync(OperationRequestBuilder.New()
            .SetDocument(query).SetUser(admin).Build());

        var json = result.ToJson();
        json.Should().NotContain("errors");
        json.Should().Contain("Root");
        json.Should().Contain("Inactive child");
        json.Should().Contain("Deleted grandchild");
    }

    [Fact]
    public async Task Admin_GraphQl_exposes_find_node_parent_and_descendants_to_admins()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin")], "test"));
        const string query = """
            {
              category(id: 3) { id name status }
              categoryParent(categoryId: 3) { id name status }
              categoryDescendants(categoryId: 1) {
                id name status
                children { id name status }
              }
            }
            """;

        var result = await executor.ExecuteAsync(OperationRequestBuilder.New()
            .SetDocument(query).SetUser(admin).Build());

        // TODO(Identity): restore anonymous-access assertions when GraphQL authorization is enabled.
        var json = result.ToJson();
        json.Should().NotContain("errors");
        json.Should().Contain("Deleted grandchild");
        json.Should().Contain("Inactive child");
    }

    [Fact]
    public async Task Admin_GraphQl_preserves_large_category_ids_as_strings()
    {
        const long categoryId = 92544908581142528L;
        var queries = new FakeAdminQueries { CategoryId = categoryId };
        await using var fixture = await ApiFixture.CreateAsync(adminQueries: queries);
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin")], "test"));

        var result = await executor.ExecuteAsync(OperationRequestBuilder.New()
            .SetDocument($"{{ category(id: \"{categoryId}\") {{ id }} }}")
            .SetUser(admin)
            .Build());

        result.ToJson().Should().Contain($"\"id\": \"{categoryId}\"");
        queries.RequestedCategoryIds.Should().Equal(categoryId);
    }

    [Fact]
    public async Task Admin_GraphQl_schema_exposes_only_the_unified_category_status_contract()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        var executor = await fixture.App.Services.GetRequestExecutorAsync();

        var mutationFields = executor.Schema.MutationType.Fields
            .Select(field => field.Name)
            .ToArray();
        mutationFields.Should().Contain([
            "updateCategory", "setCategoryStatus", "deactivateCategories", "deleteCategory"
        ]);
        mutationFields.Should().NotContain([
            "activate" + "Category", "deactivate" + "Category"
        ]);

        var statusType = executor.Schema.Types.OfType<EnumType>()
            .Single(type => type.Name == nameof(CategoryStatus));
        statusType.Values.Select(value => value.Name).Should().Equal(
            "INACTIVE", "ACTIVE", "DELETED");

        var updateInput = executor.Schema.Types.OfType<InputObjectType>()
            .Single(type => type.Name == nameof(UpdateCategoryInput));
        updateInput.Fields.Single(field => field.Name == "status")
            .Type.Should().BeSameAs(statusType);

        var statusInput = executor.Schema.Types.OfType<InputObjectType>()
            .Single(type => type.Name == "SetCategoryStatusInput");
        statusInput.Fields.Single(field => field.Name == "categoryId")
            .Type.ToString().Should().Be("ID!");
        statusInput.Fields.Single(field => field.Name == "status")
            .Type.ToString().Should().Be("CategoryStatus!");
    }

    [Fact]
    public async Task Admin_GraphQl_find_parent_and_descendants_map_not_found_errors()
    {
        var queries = new FakeAdminQueries { FailRelationshipsWithNotFound = true };
        await using var fixture = await ApiFixture.CreateAsync(adminQueries: queries);
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin")], "test"));

        var result = await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument("""
                {
                  categoryParent(categoryId: 999) { id }
                  categoryDescendants(categoryId: 999) { id }
                }
                """).SetUser(admin).Build());

        var json = result.ToJson();
        json.Split("\"code\": \"CATEGORY_NOT_FOUND\"", StringSplitOptions.None)
            .Length.Should().Be(3);
        json.Split("\"httpStatus\": 404", StringSplitOptions.None).Length.Should().Be(3);
    }

    [Fact]
    public async Task Admin_GraphQl_returns_the_persisted_Snowflake_id_and_dispatches_status_mutation()
    {
        const long persistedId = 912345678901L;
        var service = new CapturingService { CreateId = persistedId };
        var queries = new FakeAdminQueries
        {
            CategoryId = persistedId,
            Detail = new CategoryAdminDto(
                persistedId, 2, "Category", "category", null, 0, CategoryStatus.Active)
            {
                Parent = new CategoryAdminParentDto(2, "Parent"),
                ChildCount = 3
            }
        };
        await using var fixture = await ApiFixture.CreateAsync(service, queries);
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin")], "test"));

        var created = await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument(
                "mutation { createCategory(input: { name: \"Root\", slug: \"root\", displayOrder: 0 }) }")
            .SetUser(admin).Build());
        var changed = await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument(
                $"mutation {{ setCategoryStatus(input: {{ categoryId: \"{persistedId}\", status: ACTIVE }}) {{ id status parent {{ id name }} childCount }} }}")
            .SetUser(admin).Build());

        created.ToJson().Should().Contain(
            $"\"createCategory\": \"{persistedId.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"");
        changed.ToJson().Should().NotContain("errors");
        changed.ToJson().Should().Contain($"\"id\": \"{persistedId}\"");
        changed.ToJson().Should().Contain("\"status\": \"ACTIVE\"");
        changed.ToJson().Should().Contain("\"parent\": {");
        changed.ToJson().Should().Contain("\"name\": \"Parent\"");
        changed.ToJson().Should().Contain("\"childCount\": 3");
        service.StatusCommands.Should().ContainSingle().Which.Should().Be(
            new SetCategoryStatusCommand(persistedId, CategoryStatus.Active));
    }

    [Fact]
    public async Task Admin_GraphQl_update_returns_the_updated_category()
    {
        var service = new CapturingService();
        var queries = new FakeAdminQueries
        {
            CategoryId = 42,
            Detail = new CategoryAdminDto(
                42, 2, "Updated", "updated", "Updated description", 4, CategoryStatus.Active)
            {
                Parent = new CategoryAdminParentDto(2, "Parent"),
                ChildCount = 3
            }
        };
        await using var fixture = await ApiFixture.CreateAsync(service, queries);
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin")], "test"));

        var result = await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument("""
                mutation {
                  updateCategory(input: {
                    categoryId: 42
                    name: "Updated"
                    slug: "updated"
                    description: "Updated description"
                    displayOrder: 4
                  }) {
                    id parentId name slug description displayOrder status
                    parent { id name }
                    childCount
                  }
                }
                """).SetUser(admin).Build());

        var json = result.ToJson();
        json.Should().NotContain("errors");
        json.Should().Contain("\"updateCategory\": {");
        json.Should().Contain("\"id\": \"42\"");
        json.Should().Contain("\"name\": \"Updated\"");
        json.Should().Contain("\"status\": \"ACTIVE\"");
        json.Should().Contain("\"parent\": {");
        json.Should().Contain("\"name\": \"Parent\"");
        json.Should().Contain("\"childCount\": 3");
        service.UpdateCommands.Should().ContainSingle().Which.Status.Should().BeNull();
    }

    [Theory]
    [InlineData("CATEGORY_PARENT_INACTIVE", "Inactive parent.")]
    [InlineData("CATEGORY_HAS_CHILDREN", "Category has dependencies.")]
    [InlineData("CATEGORY_DELETED", "Deleted category.")]
    public async Task Set_Category_Status_GraphQl_preserves_domain_refusals(string code, string message)
    {
        await using var fixture = await ApiFixture.CreateAsync(
            new FailingService(new FrameworkError(code, message, 400)));
        var executor = await fixture.App.Services.GetRequestExecutorAsync();

        var result = await executor.ExecuteAsync("""
            mutation {
              setCategoryStatus(input: { categoryId: "7", status: ACTIVE }) { id }
            }
            """);

        var json = result.ToJson();
        json.Should().Contain($"\"code\": \"{code}\"");
        json.Should().Contain($"\"message\": \"{message}\"");
    }

    [Fact]
    public async Task Category_Mutation_Must_Surface_The_Reload_Error()
    {
        var queries = new FakeAdminQueries
        {
            DetailError = new FrameworkError(
                "CATEGORY_NOT_FOUND", "The category was not found.", 404)
        };
        await using var fixture = await ApiFixture.CreateAsync(new CapturingService(), queries);
        var executor = await fixture.App.Services.GetRequestExecutorAsync();

        var result = await executor.ExecuteAsync("""
            mutation {
              setCategoryStatus(input: { categoryId: "7", status: ACTIVE }) { id }
            }
            """);

        result.ToJson().Should().Contain("\"code\": \"CATEGORY_NOT_FOUND\"");
        queries.RequestedCategoryIds.Should().Equal(7);
    }

    [Fact]
    public async Task Set_Category_Status_rejects_an_unknown_enum_before_calling_the_service()
    {
        var service = new CapturingService();
        await using var fixture = await ApiFixture.CreateAsync(service);
        var executor = await fixture.App.Services.GetRequestExecutorAsync();

        var result = await executor.ExecuteAsync("""
            mutation {
              setCategoryStatus(input: { categoryId: "7", status: UNKNOWN }) { id }
            }
            """);

        result.ToJson().Should().Contain("errors");
        service.StatusCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_GraphQl_dispatches_all_selected_categories_to_bulk_deactivation()
    {
        var service = new CapturingService();
        await using var fixture = await ApiFixture.CreateAsync(service);
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin")], "test"));

        var result = await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument(
                "mutation { deactivateCategories(input: { categoryIds: [11, 22, 33] }) }")
            .SetUser(admin).Build());

        result.ToJson().Should().NotContain("errors");
        service.BulkDeactivatedIds.Should().Equal(11, 22, 33);
    }

    [Theory]
    [InlineData("CATEGORY_NOT_FOUND", "Missing category.", 404)]
    [InlineData("CATEGORY_PARENT_INVALID", "Invalid parent.", 400)]
    [InlineData("CATEGORY_PARENT_INACTIVE", "Inactive parent.", 400)]
    [InlineData("CATEGORY_DELETED", "Deleted category.", 400)]
    [InlineData("CATEGORY_CYCLE", "Hierarchy cycle.", 400)]
    [InlineData("CATEGORY_DEPTH_EXCEEDED", "Hierarchy too deep.", 400)]
    [InlineData("CATEGORY_SLUG_CONFLICT", "Duplicate slug.", 409)]
    [InlineData("CONCURRENCY_CONFLICT", "Concurrent update.", 409)]
    [InlineData("CATEGORY_PERSISTENCE_CONFLICT", "Persistence conflict.", 409)]
    public async Task Admin_GraphQl_serializes_complete_domain_and_persistence_errors(
        string code, string message, int httpStatus)
    {
        await using var fixture = await ApiFixture.CreateAsync(
            new FailingService(new FrameworkError(code, message, httpStatus)));
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin")], "test"));

        var result = await executor.ExecuteAsync(OperationRequestBuilder.New().SetDocument(
                "mutation { createCategory(input: { name: \"Root\", slug: \"root\", displayOrder: 0 }) }")
            .SetUser(admin).Build());
        var json = result.ToJson();

        json.Should().Contain($"\"message\": \"{message}\"");
        json.Should().Contain($"\"code\": \"{code}\"");
        json.Should().Contain($"\"httpStatus\": {httpStatus}");
    }

    [Fact]
    public async Task Admin_GraphQl_maps_a_refused_command_to_its_stable_error_code()
    {
        var mutations = new CategoryAdminMutations();
        var service = new FailingService(new FrameworkError("CATEGORY_SLUG_CONFLICT", "Slug already exists.", 409));

        var action = () => mutations.CreateCategoryAsync(service,
            new CreateCategoryInput("Offers", "offers", null, null, 0));

        var error = await action.Should().ThrowAsync<ConflictGraphQLException>();
        error.Which.Code.Should().Be("CATEGORY_SLUG_CONFLICT");
        error.Which.Message.Should().Be("Slug already exists.");
    }

    [Fact]
    public async Task Delete_Category_GraphQl_Preserves_The_Dependency_Error()
    {
        const string message =
            "امکان حذف دسته‌بندی دارای کالا یا زیردسته وجود ندارد. ابتدا وابستگی‌ها را منتقل کنید";
        var mutations = new CategoryAdminMutations();
        var service = new FailingService(new FrameworkError(
            "CATEGORY_HAS_CHILDREN", message, 400));

        var action = () => mutations.DeleteCategoryAsync(
            service, new CategoryIdInput(7));

        var error = await action.Should().ThrowAsync<ValidationGraphQLException>();
        error.Which.Code.Should().Be("CATEGORY_HAS_CHILDREN");
        error.Which.Message.Should().Be(message);
    }

    [Fact]
    public async Task Assign_Product_GraphQl_uses_the_trusted_claim_actor_and_rejects_a_client_actor_field()
    {
        var service = new CapturingService();
        await using var fixture = await ApiFixture.CreateAsync(service);
        var executor = await fixture.App.Services.GetRequestExecutorAsync();
        var trustedActorId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var attackerActorId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.NameIdentifier, trustedActorId.ToString())], "test"));

        var rejected = await executor.ExecuteAsync(OperationRequestBuilder.New()
            .SetDocument($"mutation {{ assignCategoryProduct(input: {{ categoryId: 7, productId: {ProductId}, assignedByUserId: \"{attackerActorId}\" }}) }}")
            .SetUser(principal).Build());

        rejected.ToJson().Should().Contain("assignedByUserId");
        service.AssignCommands.Should().BeEmpty();

        var accepted = await executor.ExecuteAsync(OperationRequestBuilder.New()
            .SetDocument($"mutation {{ assignCategoryProduct(input: {{ categoryId: 7, productId: {ProductId} }}) }}")
            .SetUser(principal).Build());

        accepted.ToJson().Should().NotContain("errors");
        service.AssignCommands.Should().ContainSingle().Which.AssignedByUserId.Should().Be(trustedActorId);

        var malformedPrincipal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "Admin"), new Claim("sub", "not-a-guid")], "test"));
        var missingActor = await executor.ExecuteAsync(OperationRequestBuilder.New()
            .SetDocument($"mutation {{ assignCategoryProduct(input: {{ categoryId: 7, productId: {ProductId} }}) }}")
            .SetUser(malformedPrincipal).Build());

        missingActor.ToJson().Should().NotContain("errors");
        service.AssignCommands.Last().AssignedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task Public_routes_are_exact_and_reject_out_of_range_product_pages_before_querying()
    {
        await using var fixture = await ApiFixture.CreateAsync();

        fixture.Endpoints.Select(endpoint => endpoint.RoutePattern.RawText).Should().Contain([
            "/api/v1/categories/tree",
            "/api/v1/categories/{id:long}",
            "/api/v1/categories/{id:long}/products",
            "/api/v1/products/{productId:long}/categories"
        ]);

        var response = await fixture.InvokeAsync(
            "/api/v1/categories/{id:long}/products", "/api/v1/categories/7/products?page=0&pageSize=101");

        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        using var error = JsonDocument.Parse(response.Body);
        error.RootElement.GetProperty("code").GetString()
            .Should().Be("CATEGORY_PAGE_INVALID");
        fixture.Queries.ProductPageCalls.Should().Be(0,
            "the public adapter must reject invalid paging without issuing an unbounded query");
    }

    [Fact]
    public async Task Public_product_page_exposes_only_product_ids_and_maps_query_errors()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        fixture.Queries.ProductPage = Result.Success(new CategoryProductPageDto([ProductId], 1, 20, 1));

        var response = await fixture.InvokeAsync(
            "/api/v1/categories/{id:long}/products", "/api/v1/categories/7/products?page=1&pageSize=20");

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        using var document = JsonDocument.Parse(response.Body);
        document.RootElement.GetProperty("data").GetProperty("productIds")
            .EnumerateArray().Single()
            .GetInt64().Should().Be(ProductId);
        document.RootElement.GetProperty("data").TryGetProperty("products", out _).Should().BeFalse(
            "Catalog product details are owned by Catalog and must not be duplicated by this read adapter");

        fixture.Queries.Category = Result.Failure<CategoryDto>(
            new FrameworkError("CATEGORY_NOT_FOUND", "Missing.", 404));
        var notFound = await fixture.InvokeAsync(
            "/api/v1/categories/{id:long}", "/api/v1/categories/999");

        notFound.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        using var error = JsonDocument.Parse(notFound.Body);
        error.RootElement.GetProperty("code").GetString().Should().Be("CATEGORY_NOT_FOUND");
        error.RootElement.GetProperty("message").GetString().Should().Be("Missing.");
    }

    [Fact]
    public async Task Public_tree_and_reverse_product_routes_execute_the_effectively_visible_query_results()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        fixture.Queries.Tree = [new CategoryTreeNodeDto(7, null, "Offers", "offers", null, 0, [])];
        fixture.Queries.ProductCategories = [new CategoryDto(7, null, "Offers", "offers", null, 0)];

        var tree = await fixture.InvokeAsync("/api/v1/categories/tree", "/api/v1/categories/tree");
        tree.StatusCode.Should().Be(StatusCodes.Status200OK);
        using var treeDocument = JsonDocument.Parse(tree.Body);
        treeDocument.RootElement.GetProperty("message").GetString()
            .Should().Be("Operation completed successfully.");
        treeDocument.RootElement.GetProperty("data").EnumerateArray().Single()
            .GetProperty("slug").GetString().Should().Be("offers");

        var reverse = await fixture.InvokeAsync("/api/v1/products/{productId:long}/categories",
            $"/api/v1/products/{ProductId}/categories");
        reverse.StatusCode.Should().Be(StatusCodes.Status200OK);
        using var reverseDocument = JsonDocument.Parse(reverse.Body);
        reverseDocument.RootElement.GetProperty("data").EnumerateArray().Single()
            .GetProperty("slug").GetString().Should().Be("offers");
    }

    private sealed class ApiFixture : IAsyncDisposable
    {
        private ApiFixture(WebApplication app, FakeQueries queries)
        {
            App = app;
            Queries = queries;
        }

        public WebApplication App { get; }
        public FakeQueries Queries { get; }
        public IReadOnlyList<RouteEndpoint> Endpoints => ((IEndpointRouteBuilder)App).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        public static Task<ApiFixture> CreateAsync(ICategoryService? service = null,
            ICategoryAdminQueries? adminQueries = null)
        {
            var builder = WebApplication.CreateBuilder();
            var queries = new FakeQueries();
            adminQueries ??= new FakeAdminQueries();
            builder.Services.AddAuthorization(options => options.AddPolicy("Admin", policy => policy.RequireRole("Admin")));
            builder.Services.AddSingleton<ICategoryQueries>(queries);
            builder.Services.AddSingleton(adminQueries);
            builder.Services.AddSingleton<ICategoryService>(service ?? new FailingService(FrameworkError.Internal));
            builder.Services
                .AddCatalogGraphQL();
            builder.Services.AddCatalogRestAdapter();
            var app = builder.Build();
            app.MapCategoryPublicEndpoints();
            app.MapGraphQLEndpoint();
            return Task.FromResult(new ApiFixture(app, queries));
        }

        public async Task<(int StatusCode, string Body)> InvokeAsync(string routePattern, string pathAndQuery)
        {
            var endpoint = Endpoints.Single(candidate => candidate.RoutePattern.RawText == routePattern);
            var context = new DefaultHttpContext { RequestServices = App.Services };
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test"));
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = pathAndQuery.Split('?', 2)[0];
            context.Request.QueryString = pathAndQuery.Contains('?')
                ? new QueryString("?" + pathAndQuery.Split('?', 2)[1])
                : QueryString.Empty;
            if (routePattern.Contains("{id:long}", StringComparison.Ordinal))
                context.Request.RouteValues["id"] = pathAndQuery.Split('?', 2)[0].Split('/')[4];
            if (routePattern.Contains("{productId:long}", StringComparison.Ordinal))
                context.Request.RouteValues["productId"] = pathAndQuery.Split('?', 2)[0].Split('/')[4];
            context.Response.Body = new MemoryStream();

            await endpoint.RequestDelegate!(context);
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            return (context.Response.StatusCode, await reader.ReadToEndAsync());
        }

        public async ValueTask DisposeAsync() => await App.DisposeAsync();
    }

    private static bool HasAdminPolicy(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(metadata => metadata.Policy == "Admin");

    private sealed class FakeQueries : ICategoryQueries
    {
        public Result<CategoryDto> Category { get; set; } = Result.Success(new CategoryDto(7, null, "Offers", "offers", null, 0));
        public Result<CategoryProductPageDto> ProductPage { get; set; } = Result.Success(
            new CategoryProductPageDto([], 1, 20, 0));
        public int ProductPageCalls { get; private set; }
        public IReadOnlyList<CategoryTreeNodeDto> Tree { get; set; } = [];
        public IReadOnlyList<CategoryDto> ProductCategories { get; set; } = [];

        public Task<IReadOnlyList<CategoryTreeNodeDto>> GetTreeAsync(CancellationToken ct = default) =>
            Task.FromResult(Tree);
        public Task<Result<CategoryDto>> GetByIdAsync(long categoryId, CancellationToken ct = default) => Task.FromResult(Category);
        public Task<Result<CategoryProductPageDto>> GetProductIdsAsync(long categoryId, int page, int pageSize,
            CancellationToken ct = default)
        {
            ProductPageCalls++;
            return Task.FromResult(ProductPage);
        }
        public Task<IReadOnlyList<CategoryDto>> GetByProductIdAsync(long productId, CancellationToken ct = default) =>
            Task.FromResult(ProductCategories);
    }

    private sealed class FakeAdminQueries : ICategoryAdminQueries
    {
        private static readonly CategoryAdminDto Deleted =
            new(7, null, "Retired", "retired", null, 0, CategoryStatus.Deleted);
        private static readonly CategoryAdminDto Enriched = Deleted with
        {
            ParentId = 2,
            Parent = new CategoryAdminParentDto(2, "Parent"),
            ChildCount = 3
        };
        private static readonly FrameworkError Missing =
            new("CATEGORY_NOT_FOUND", "The category was not found.", 404);

        public bool FailRelationshipsWithNotFound { get; init; }
        public bool ProductCategoryExists { get; init; } = true;
        public long CategoryId { get; init; } = Deleted.Id;
        public CategoryAdminDto Detail { get; init; } = Enriched;
        public FrameworkError? DetailError { get; init; }
        public List<long> RequestedCategoryIds { get; } = [];

        public IQueryable<CategoryAdminDto> Search(CategoryAdminFilterDto? filter) =>
            new[] { Enriched, Deleted with { Id = 8, Name = "Archived" } }.AsQueryable();
        public Task<Result<CategoryAdminDto>> GetByIdAsync(long categoryId, CancellationToken ct = default)
        {
            RequestedCategoryIds.Add(categoryId);
            return Task.FromResult(DetailError is null
                ? Result.Success(Detail with { Id = CategoryId })
                : Result.Failure<CategoryAdminDto>(DetailError));
        }
        public Task<Result<CategoryAdminDto?>> GetParentAsync(long categoryId,
            CancellationToken ct = default) => Task.FromResult(FailRelationshipsWithNotFound
                ? Result.Failure<CategoryAdminDto?>(Missing)
                : Result.Success<CategoryAdminDto?>(new CategoryAdminDto(
                    2, 1, "Inactive child", "inactive-child", null, 0, CategoryStatus.Inactive)));
        public Task<Result<IReadOnlyList<CategoryAdminTreeNodeDto>>> GetDescendantsAsync(
            long categoryId, CancellationToken ct = default) => Task.FromResult(FailRelationshipsWithNotFound
                ? Result.Failure<IReadOnlyList<CategoryAdminTreeNodeDto>>(Missing)
                : Result.Success<IReadOnlyList<CategoryAdminTreeNodeDto>>([
                    new(2, 1, "Inactive child", "inactive-child", null, 0, CategoryStatus.Inactive, [
                        new(3, 2, "Deleted grandchild", "deleted-grandchild", null, 0,
                            CategoryStatus.Deleted, [])
                    ])
                ]));
        public Task<IReadOnlyList<CategoryAdminDto>> GetChildrenAsync(long? parentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CategoryAdminDto>>([Deleted]);
        public Task<IReadOnlyList<CategoryAdminTreeNodeDto>> GetHierarchyAsync(
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CategoryAdminTreeNodeDto>>([
                new(1, null, "Root", "root", null, 0, CategoryStatus.Active, [
                    new(2, 1, "Inactive child", "inactive-child", null, 0, CategoryStatus.Inactive, [
                        new(3, 2, "Deleted grandchild", "deleted-grandchild", null, 0, CategoryStatus.Deleted, [])
                    ])
                ])
            ]);
        public Task<Result<IQueryable<long>>> GetProductIdsAsync(long categoryId,
            CancellationToken ct = default) => Task.FromResult(ProductCategoryExists
                ? Result.Success<IQueryable<long>>(new long[] { 10, 20 }.AsQueryable())
                : Result.Failure<IQueryable<long>>(Missing));
    }

    private sealed class FailingService(FrameworkError error) : ICategoryService
    {
        public Task<Result<long>> CreateAsync(CreateCategoryCommand command, CancellationToken ct = default) =>
            Task.FromResult(Result.Failure<long>(error));
        public Task<Result<CategoryAdminDto>> UpdateAsync(
            UpdateCategoryCommand command, CancellationToken ct = default) =>
            Task.FromResult(Result.Failure<CategoryAdminDto>(error));
        public Task<Result<CategoryAdminDto>> SetStatusAsync(
            SetCategoryStatusCommand command, CancellationToken ct = default) =>
            Task.FromResult(Result.Failure<CategoryAdminDto>(error));
        public Task<Result> MoveAsync(MoveCategoryCommand command, CancellationToken ct = default) => Task.FromResult(Result.Failure(error));
        public Task<Result> DeactivateManyAsync(BulkDeactivateCategoriesCommand command,
            CancellationToken ct = default) => Task.FromResult(Result.Failure(error));
        public Task<Result> DeleteAsync(DeleteCategoryCommand command, CancellationToken ct = default) => Task.FromResult(Result.Failure(error));
        public Task<Result> AssignProductAsync(AssignCategoryProductCommand command, CancellationToken ct = default) => Task.FromResult(Result.Failure(error));
        public Task<Result> UnassignProductAsync(UnassignCategoryProductCommand command, CancellationToken ct = default) => Task.FromResult(Result.Failure(error));
    }

    private sealed class CapturingService : ICategoryService
    {
        public List<AssignCategoryProductCommand> AssignCommands { get; } = [];
        public List<UpdateCategoryCommand> UpdateCommands { get; } = [];
        public List<SetCategoryStatusCommand> StatusCommands { get; } = [];
        public long CreateId { get; init; } = 1L;
        public List<long> BulkDeactivatedIds { get; } = [];
        public Task<Result<long>> CreateAsync(CreateCategoryCommand command, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(CreateId));
        public Task<Result<CategoryAdminDto>> UpdateAsync(
            UpdateCategoryCommand command, CancellationToken ct = default)
        {
            UpdateCommands.Add(command);
            return Task.FromResult(Result.Success(new CategoryAdminDto(
                command.CategoryId, null, command.Name, command.Slug, command.Description,
                command.DisplayOrder, command.Status ?? CategoryStatus.Active)));
        }
        public Task<Result<CategoryAdminDto>> SetStatusAsync(
            SetCategoryStatusCommand command, CancellationToken ct = default)
        {
            StatusCommands.Add(command);
            return Task.FromResult(Result.Success(new CategoryAdminDto(
                command.CategoryId, null, "Category", "category", null, 0, command.Status)));
        }
        public Task<Result> MoveAsync(MoveCategoryCommand command, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> DeactivateManyAsync(BulkDeactivateCategoriesCommand command,
            CancellationToken ct = default)
        {
            BulkDeactivatedIds.AddRange(command.CategoryIds);
            return Task.FromResult(Result.Success());
        }
        public Task<Result> DeleteAsync(DeleteCategoryCommand command, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> AssignProductAsync(AssignCategoryProductCommand command, CancellationToken ct = default)
        {
            AssignCommands.Add(command);
            return Task.FromResult(Result.Success());
        }
        public Task<Result> UnassignProductAsync(UnassignCategoryProductCommand command, CancellationToken ct = default) => Task.FromResult(Result.Success());
    }
}
