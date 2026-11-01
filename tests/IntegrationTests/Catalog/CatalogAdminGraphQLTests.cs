using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Catalog;

/// <summary>
/// The catalogue's admin GraphQL. Every resolver carries its own gate — a type-level attribute on
/// an <c>[ExtendObjectType]</c> would be merged onto the whole Query and gate every other
/// module's fields too — so the gating is asserted here rather than taken on trust.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class CatalogAdminGraphQLTests(PostgresFixture fixture)
{
    private static HttpClient StaffClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateStaffToken());

        return client;
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<(long BrandId, long CategoryId)> SeedAsync(
        WebApplicationFactory<Program> factory, string suffix)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNext.Database.EwaysVNextDbContext>();

        var brand = EwaysVNext.Modules.Catalog.Application.Brands.Domain.Brand.Create(
            Random.Shared.NextInt64(400_000_000, 499_999_999), $"Brand {suffix}", "seed",
            DateTimeOffset.UtcNow);
        var category = EwaysVNext.Modules.Catalog.Application.Categories.Domain.Category.Create(
            Random.Shared.NextInt64(400_000_000, 499_999_999), $"Kettles {suffix}",
            $"kettles-{suffix}", null, null, 0, DateTimeOffset.UtcNow);

        db.Add(brand);
        db.Add(category);
        await db.SaveChangesAsync();

        return (brand.Id, category.Id);
    }

    private const string CreateProduct = """
        mutation Create($input: CreateProductInput!) {
          createProduct(input: $input) { id title status activationProblems }
        }
        """;

    [Fact]
    public async Task CreateProduct_Then_AdminProduct_Returns_Draft()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);
        var suffix = Suffix();
        var (brandId, categoryId) = await SeedAsync(factory, suffix);

        var created = await AdminGraphQLClient.PostForResultAsync<JsonElement>(
            client, CreateProduct,
            new
            {
                input = new
                {
                    brandId = brandId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    primaryCategoryId = categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    title = $"GraphQL Kettle {suffix}",
                },
            },
            TestContext.Current.CancellationToken);

        created.Errors.Should().BeNullOrEmpty();
        var product = created.Data.GetProperty("createProduct");
        product.GetProperty("status").GetString().Should().Be("Draft");

        // The mutation answered with the product read back through the same projection the
        // query uses, so asking again must agree with what it said.
        var id = product.GetProperty("id").GetString();
        var fetched = await AdminGraphQLClient.PostForResultAsync<JsonElement>(
            client,
            """
            query Get($id: ID!) { adminProduct(id: $id) { id title status activationProblems } }
            """,
            new { id },
            TestContext.Current.CancellationToken);

        fetched.Errors.Should().BeNullOrEmpty();
        var fetchedProduct = fetched.Data.GetProperty("adminProduct");
        fetchedProduct.GetProperty("id").GetString().Should().Be(id);
        fetchedProduct.GetProperty("status").GetString().Should().Be("Draft");
        fetchedProduct.GetProperty("activationProblems").EnumerateArray()
            .Select(problem => problem.GetString())
            .Should().Contain(["NO_ACTIVE_VARIANT", "NO_USABLE_IMAGE"]);
    }

    [Fact]
    public async Task Anonymous_AdminProducts_Returns_AUTH_NOT_AUTHORIZED()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await AdminGraphQLClient.PostAsync(
            client,
            "query { adminProducts(page: 1, pageSize: 10) { totalCount } }",
            ct: TestContext.Current.CancellationToken);

        var code = await AdminGraphQLClient.FirstErrorCodeAsync(
            response, TestContext.Current.CancellationToken);

        code.Should().BeOneOf("AUTH_NOT_AUTHORIZED", "AUTH_NOT_AUTHENTICATED");
    }

    [Fact]
    public async Task A_Shop_Customer_Cannot_Read_Admin_Products()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateCustomerToken());

        using var response = await AdminGraphQLClient.PostAsync(
            client,
            "query { adminProducts(page: 1, pageSize: 10) { totalCount } }",
            ct: TestContext.Current.CancellationToken);

        var code = await AdminGraphQLClient.FirstErrorCodeAsync(
            response, TestContext.Current.CancellationToken);

        code.Should().Be("AUTH_NOT_AUTHORIZED",
            "the Shop realm is open self-service registration, so its tokens carry no backoffice "
            + "meaning whatever roles they claim");
    }

    [Fact]
    public async Task SetProductActive_Without_Image_Returns_PRODUCT_ACTIVATION_INVALID()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);
        var suffix = Suffix();
        var (brandId, categoryId) = await SeedAsync(factory, suffix);

        var created = await AdminGraphQLClient.PostForResultAsync<JsonElement>(
            client, CreateProduct,
            new
            {
                input = new
                {
                    brandId = brandId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    primaryCategoryId = categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    title = $"Imageless {suffix}",
                },
            },
            TestContext.Current.CancellationToken);

        var id = created.Data.GetProperty("createProduct").GetProperty("id").GetString();

        using var response = await AdminGraphQLClient.PostAsync(
            client,
            """
            mutation Activate($input: SetProductActiveInput!) {
              setProductActive(input: $input) { id status }
            }
            """,
            new { input = new { productId = id, active = true } },
            TestContext.Current.CancellationToken);

        var code = await AdminGraphQLClient.FirstErrorCodeAsync(
            response, TestContext.Current.CancellationToken);

        code.Should().Be("PRODUCT_ACTIVATION_INVALID");
    }

    [Fact]
    public async Task Category_Admin_Mutations_Are_Gated_Too()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateCustomerToken());

        using var response = await AdminGraphQLClient.PostAsync(
            client,
            """
            mutation { createCategory(input: { name: "Sneaky", slug: "sneaky", displayOrder: 0 }) }
            """,
            ct: TestContext.Current.CancellationToken);

        var code = await AdminGraphQLClient.FirstErrorCodeAsync(
            response, TestContext.Current.CancellationToken);

        // This holds through the permission check alone, because BackofficeRoles.From returns
        // nothing for a Shop token and a caller with no permissions is refused. The [Authorize]
        // attribute these resolvers shipped without is belt-and-braces against a misedited
        // permission row, which is not observable from outside — so this asserts the outcome,
        // not which of the two gates produced it.
        code.Should().Be("AUTH_NOT_AUTHORIZED");
    }

    [Fact]
    public async Task A_Staff_Token_Can_Reach_The_Category_Admin_Surface()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);

        var result = await AdminGraphQLClient.PostForResultAsync<JsonElement>(
            client,
            "query { categoryHierarchy { id parentId } }",
            ct: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNullOrEmpty(
            "regating must not have locked staff out of what they could already do");
    }
}
