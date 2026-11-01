using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Catalog;

/// <summary>
/// The product admin surface end to end: create, add a variant, upload an image, go active.
/// <para>
/// These run against the fixture's real S3 server rather than a fake, so the object a successful
/// upload claims to have written is checked where it would actually be.
/// </para>
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class CatalogProductRestTests(PostgresFixture fixture)
{
    private const string Products = "/api/v1/admin/catalog/products";
    private const string Brands = "/api/v1/admin/catalog/brands";

    /// <summary>A minimal but structurally genuine PNG header.</summary>
    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private static HttpClient StaffClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateStaffToken());

        return client;
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static long IdFromLocation(HttpResponseMessage response) =>
        long.Parse(response.Headers.Location!.ToString().Split('/')[^1],
            System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<long> CreateBrandAsync(HttpClient client, string suffix)
    {
        using var response = await client.PostAsJsonAsync(Brands, new { displayName = $"Brand {suffix}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return IdFromLocation(response);
    }

    /// <summary>
    /// A category straight into the database: the category admin REST surface is task 14's other
    /// half and this test is about products, not about how a category came to exist.
    /// </summary>
    private static async Task<long> CreateCategoryAsync(WebApplicationFactory<Program> factory, string suffix)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNext.Database.EwaysVNextDbContext>();

        var category = EwaysVNext.Modules.Catalog.Application.Categories.Domain.Category.Create(
            Random.Shared.NextInt64(600_000_000, 699_999_999), $"Kettles {suffix}",
            $"kettles-{suffix}", null, null, 0, DateTimeOffset.UtcNow);

        db.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private static MultipartFormDataContent ImageForm(byte[] bytes, string rights, string fileName = "kettle.png")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", fileName);
        content.Add(new StringContent("a kettle"), "alt");
        content.Add(new StringContent(rights), "rights");
        return content;
    }

    [Fact]
    public async Task Staff_Creates_Product_Variant_Uploads_Image_And_Activates()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);
        var suffix = Suffix();

        var brandId = await CreateBrandAsync(client, suffix);
        var categoryId = await CreateCategoryAsync(factory, suffix);

        using var created = await client.PostAsJsonAsync(Products, new
        {
            brandId,
            primaryCategoryId = categoryId,
            title = $"Demo Kettle {suffix}",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var productId = IdFromLocation(created);

        using var variant = await client.PostAsJsonAsync(
            $"{Products}/{productId}/variants", new { sku = $"SKU-{suffix}" });
        variant.StatusCode.Should().Be(HttpStatusCode.Created);

        using var image = await client.PostAsync(
            $"{Products}/{productId}/media", ImageForm(Png, "Allowed"));
        image.StatusCode.Should().Be(HttpStatusCode.Created);

        using var activated = await client.PutAsJsonAsync(
            $"{Products}/{productId}/status", new { isActive = true });
        activated.StatusCode.Should().Be(HttpStatusCode.OK);

        using var detail = await client.GetAsync($"{Products}/{productId}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

        body.GetProperty("status").GetString().Should().Be("Active");
        body.GetProperty("activationProblems").GetArrayLength().Should().Be(0);

        var media = body.GetProperty("media");
        media.GetArrayLength().Should().Be(1);
        var objectKey = media[0].GetProperty("objectKey").GetString()!;

        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        (await storage.ExistsAsync(StorageBuckets.CatalogMedia, objectKey))
            .Should().BeTrue("a saved media row must point at an object that is actually there");
    }

    [Fact]
    public async Task Activating_Without_A_Usable_Image_Says_What_Is_Missing()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);
        var suffix = Suffix();

        var brandId = await CreateBrandAsync(client, suffix);
        var categoryId = await CreateCategoryAsync(factory, suffix);

        using var created = await client.PostAsJsonAsync(Products, new
        {
            brandId,
            primaryCategoryId = categoryId,
            title = $"Imageless {suffix}",
        });
        var productId = IdFromLocation(created);

        using var variant = await client.PostAsJsonAsync(
            $"{Products}/{productId}/variants", new { sku = $"SKX-{suffix}" });
        variant.StatusCode.Should().Be(HttpStatusCode.Created);

        // Rights unknown is deliberately not the same as allowed: nobody has confirmed the
        // seller may publish this picture.
        using var image = await client.PostAsync(
            $"{Products}/{productId}/media", ImageForm(Png, "RightsUnknown"));
        image.StatusCode.Should().Be(HttpStatusCode.Created);

        using var activated = await client.PutAsJsonAsync(
            $"{Products}/{productId}/status", new { isActive = true });

        activated.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await activated.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("PRODUCT_ACTIVATION_INVALID");
        body.GetProperty("message").GetString().Should().Contain("NO_USABLE_IMAGE");
    }

    [Fact]
    public async Task Upload_Text_File_Returns_415_MEDIA_TYPE_UNSUPPORTED()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);
        var suffix = Suffix();

        var brandId = await CreateBrandAsync(client, suffix);
        var categoryId = await CreateCategoryAsync(factory, suffix);

        using var created = await client.PostAsJsonAsync(Products, new
        {
            brandId,
            primaryCategoryId = categoryId,
            title = $"Texty {suffix}",
        });
        var productId = IdFromLocation(created);

        // Named and declared as a PNG; the bytes say otherwise, and the bytes are what count.
        using var upload = await client.PostAsync(
            $"{Products}/{productId}/media",
            ImageForm("this is not a picture"u8.ToArray(), "Allowed"));

        upload.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await upload.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("MEDIA_TYPE_UNSUPPORTED");
    }

    [Fact]
    public async Task Bulk_Returns_Per_Item_Outcomes()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);
        var suffix = Suffix();

        var brandId = await CreateBrandAsync(client, suffix);
        var categoryId = await CreateCategoryAsync(factory, suffix);

        using var ready = await client.PostAsJsonAsync(Products, new
        {
            brandId, primaryCategoryId = categoryId, title = $"Ready {suffix}",
        });
        var readyId = IdFromLocation(ready);
        using var readyVariant = await client.PostAsJsonAsync(
            $"{Products}/{readyId}/variants", new { sku = $"SKR-{suffix}" });
        readyVariant.StatusCode.Should().Be(HttpStatusCode.Created);
        using var readyImage = await client.PostAsync(
            $"{Products}/{readyId}/media", ImageForm(Png, "Allowed"));
        readyImage.StatusCode.Should().Be(HttpStatusCode.Created);

        using var bare = await client.PostAsJsonAsync(Products, new
        {
            brandId, primaryCategoryId = categoryId, title = $"Bare {suffix}",
        });
        var bareId = IdFromLocation(bare);

        using var bulk = await client.PostAsJsonAsync($"{Products}/bulk", new
        {
            productIds = new[] { readyId, bareId },
            status = "activate",
        });

        bulk.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await bulk.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("items");

        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("succeeded").GetBoolean().Should().BeTrue();
        items[1].GetProperty("succeeded").GetBoolean().Should().BeFalse();
        items[1].GetProperty("errorCode").GetString().Should().Be("PRODUCT_ACTIVATION_INVALID");

        // The one that failed is untouched, not half-applied.
        using var bareDetail = await client.GetAsync($"{Products}/{bareId}");
        (await bareDetail.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("status").GetString().Should().Be("Draft");
    }

    [Fact]
    public async Task Customer_Token_Gets_403_On_Admin_Catalog()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AdminJwtFactory.CreateCustomerToken());

        using var response = await client.GetAsync(Products);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Unknown_Status_Filter_Is_Refused_Rather_Than_Ignored()
    {
        using var factory = fixture.CreateFactory();
        using var client = StaffClient(factory);

        using var response = await client.GetAsync($"{Products}?status=published");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "silently dropping a filter shows the admin a list that is not what they asked for");
    }
}
