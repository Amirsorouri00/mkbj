using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Brands.Domain;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Catalog;

/// <summary>
/// The dedup guarantees the product tables make on their own.
/// <para>
/// These are the catalogue's last line against importing the same article twice. The aggregate
/// can only see one product at a time, so "no two products share a GTIN" is something only the
/// database can promise — and an import writing a thousand rows is exactly where a race or a
/// fix-up script gets past the aggregate.
/// </para>
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class ProductConstraintTests(PostgresFixture fixture)
{
    private static long NextId() => Random.Shared.NextInt64(700_000_000, 799_999_999);

    /// <summary>
    /// A fresh, check-digit-valid GTIN-13 per test. A hard-coded one collides across tests: these
    /// share a database, and the GS1 index is global by design.
    /// </summary>
    private static string NewGtin13()
    {
        var payload = Random.Shared.NextInt64(100_000_000_000, 999_999_999_999)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        var sum = 0;
        var weight = 3;
        for (var index = payload.Length - 1; index >= 0; index--)
        {
            sum += (payload[index] - '0') * weight;
            weight = weight == 3 ? 1 : 3;
        }

        return payload + (10 - (sum % 10)) % 10;
    }

    private sealed record Fixtures(long BrandId, long CategoryId);

    /// <summary>A brand and a category for products to hang off, since both are FK targets.</summary>
    private static async Task<Fixtures> SeedAsync(IServiceProvider services, string suffix)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        var brand = Brand.Create(NextId(), $"Brand {suffix}", "seed", DateTimeOffset.UtcNow);
        var category = Category.Create(
            NextId(), $"Category {suffix}", $"category-{suffix}", null, null, 0, DateTimeOffset.UtcNow);

        db.Set<Brand>().Add(brand);
        db.Set<Category>().Add(category);
        await db.SaveChangesAsync();

        return new Fixtures(brand.Id, category.Id);
    }

    private static Product Draft(Fixtures fixtures, string title, string slug) =>
        Product.CreateDraft(NextId(), fixtures.BrandId, fixtures.CategoryId, title, slug, null, null,
            DateTimeOffset.UtcNow);

    private static async Task SaveAsync(IServiceProvider services, Product product)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        db.Set<Product>().Add(product);
        await db.SaveChangesAsync();
    }

    private static async Task<PostgresException> ExpectUniqueViolationAsync(Func<Task> act)
    {
        var postgres = (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<PostgresException>().Which;

        postgres.SqlState.Should().Be("23505");
        return postgres;
    }

    [Fact]
    public async Task Duplicate_Gtin_Across_Products_Is_Rejected()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fixtures = await SeedAsync(factory.Services, suffix);

        var gtin = NewGtin13();

        var first = Draft(fixtures, "Kettle A", $"kettle-a-{suffix}");
        first.AddIdentifier(NextId(), ProductIdentifierType.Gtin, gtin, null, DateTimeOffset.UtcNow);
        await SaveAsync(factory.Services, first);

        var second = Draft(fixtures, "Kettle B", $"kettle-b-{suffix}");
        second.AddIdentifier(NextId(), ProductIdentifierType.Gtin, gtin, null, DateTimeOffset.UtcNow);

        var postgres = await ExpectUniqueViolationAsync(() => SaveAsync(factory.Services, second));

        postgres.ConstraintName.Should().Be("ux_product_identifiers_type_value",
            "a GTIN identifies the article worldwide, so two products holding one is a duplicate");
    }

    [Fact]
    public async Task Duplicate_Brand_Mpn_Is_Rejected()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fixtures = await SeedAsync(factory.Services, suffix);

        var first = Draft(fixtures, "Kettle A", $"mpn-a-{suffix}");
        first.AddIdentifier(NextId(), ProductIdentifierType.Mpn, $"MX-{suffix}", null, DateTimeOffset.UtcNow);
        await SaveAsync(factory.Services, first);

        var second = Draft(fixtures, "Kettle B", $"mpn-b-{suffix}");
        second.AddIdentifier(NextId(), ProductIdentifierType.Mpn, $"mx-{suffix}", null, DateTimeOffset.UtcNow);

        var postgres = await ExpectUniqueViolationAsync(() => SaveAsync(factory.Services, second));

        postgres.ConstraintName.Should().Be("ux_products_brand_mpn");
    }

    [Fact]
    public async Task The_Same_Mpn_Under_Two_Brands_Is_Allowed()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var first = await SeedAsync(factory.Services, $"one-{suffix}");
        var second = await SeedAsync(factory.Services, $"two-{suffix}");

        var a = Draft(first, "Kettle A", $"two-brands-a-{suffix}");
        a.AddIdentifier(NextId(), ProductIdentifierType.Mpn, $"MX-{suffix}", null, DateTimeOffset.UtcNow);
        await SaveAsync(factory.Services, a);

        var b = Draft(second, "Kettle B", $"two-brands-b-{suffix}");
        b.AddIdentifier(NextId(), ProductIdentifierType.Mpn, $"MX-{suffix}", null, DateTimeOffset.UtcNow);

        var act = () => SaveAsync(factory.Services, b);

        await act.Should().NotThrowAsync(
            "the same part number under two brands is two different parts, which is why the MPN "
            + "index is on the brand pair and not on the identifier table");
    }

    [Fact]
    public async Task Duplicate_Source_Domain_And_Id_Is_Rejected()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fixtures = await SeedAsync(factory.Services, suffix);

        var first = Draft(fixtures, "Listing A", $"src-a-{suffix}");
        first.AddIdentifier(NextId(), ProductIdentifierType.SourceId, $"SKU{suffix}", "example.com", DateTimeOffset.UtcNow);
        await SaveAsync(factory.Services, first);

        var second = Draft(fixtures, "Listing B", $"src-b-{suffix}");
        second.AddIdentifier(NextId(), ProductIdentifierType.SourceId, $"SKU{suffix}", "EXAMPLE.com", DateTimeOffset.UtcNow);

        var postgres = await ExpectUniqueViolationAsync(() => SaveAsync(factory.Services, second));

        postgres.ConstraintName.Should().Be("ux_product_identifiers_source",
            "the domain is lower-cased on the way in, so two casings are one source");
    }

    [Fact]
    public async Task The_Same_Source_Id_From_Two_Domains_Is_Allowed()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fixtures = await SeedAsync(factory.Services, suffix);

        var first = Draft(fixtures, "Listing A", $"two-src-a-{suffix}");
        first.AddIdentifier(NextId(), ProductIdentifierType.SourceId, $"SKU{suffix}", "one.example", DateTimeOffset.UtcNow);
        await SaveAsync(factory.Services, first);

        var second = Draft(fixtures, "Listing B", $"two-src-b-{suffix}");
        second.AddIdentifier(NextId(), ProductIdentifierType.SourceId, $"SKU{suffix}", "two.example", DateTimeOffset.UtcNow);

        var act = () => SaveAsync(factory.Services, second);

        await act.Should().NotThrowAsync("a source id is only unique within the system it came from");
    }

    [Fact]
    public async Task Same_Gtin_Value_As_Mpn_Is_Allowed()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fixtures = await SeedAsync(factory.Services, suffix);

        var value = NewGtin13();

        var first = Draft(fixtures, "Kettle A", $"mixed-a-{suffix}");
        first.AddIdentifier(NextId(), ProductIdentifierType.Gtin, value, null, DateTimeOffset.UtcNow);
        await SaveAsync(factory.Services, first);

        var second = Draft(fixtures, "Kettle B", $"mixed-b-{suffix}");
        second.AddIdentifier(NextId(), ProductIdentifierType.Mpn, value, null, DateTimeOffset.UtcNow);

        var act = () => SaveAsync(factory.Services, second);

        await act.Should().NotThrowAsync(
            "the GS1 index is filtered to the GS1 types; a part number that happens to look like "
            + "a GTIN is a different namespace");
    }

    [Fact]
    public async Task Duplicate_Sku_Across_Products_Is_Rejected()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fixtures = await SeedAsync(factory.Services, suffix);
        var sku = $"SKU-{suffix}";

        var first = Draft(fixtures, "Kettle A", $"sku-a-{suffix}");
        first.AddVariant(NextId(), sku, new Dictionary<string, string>(), DateTimeOffset.UtcNow);
        await SaveAsync(factory.Services, first);

        var second = Draft(fixtures, "Kettle B", $"sku-b-{suffix}");
        second.AddVariant(NextId(), sku, new Dictionary<string, string>(), DateTimeOffset.UtcNow);

        var postgres = await ExpectUniqueViolationAsync(() => SaveAsync(factory.Services, second));

        postgres.ConstraintName.Should().Be("ux_variants_sku",
            "a SKU is what a picker reads off a shelf; the aggregate can only see one product");
    }

    [Fact]
    public async Task Duplicate_Variant_Signature_In_Product_Is_Rejected_By_Database()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fixtures = await SeedAsync(factory.Services, suffix);

        var product = Draft(fixtures, "Tee", $"sig-{suffix}");
        var variant = product.AddVariant(
            NextId(), $"SKA{suffix}", new Dictionary<string, string> { ["Size"] = "M" },
            DateTimeOffset.UtcNow);
        await SaveAsync(factory.Services, product);

        // Written past the aggregate, which is the only way this can happen in practice: two
        // import workers claiming the same feed row at the same moment.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        var newVariantId = NextId();
        var otherSku = $"SKB{suffix}".ToUpperInvariant();
        var signature = variant.AttributeSignature;
        var hash = variant.AttributeSignatureHash;
        // Passed as a parameter rather than written inline: a brace is an interpolation hole.
        var emptyJson = "{}";

        var act = async () => await db.Database.ExecuteSqlAsync($"""
            INSERT INTO catalog.variants
                (id, product_id, sku, attribute_signature, attribute_signature_hash,
                 attribute_values, is_active, deactivated_by_admin, created_at)
            VALUES ({newVariantId}, {product.Id}, {otherSku}, {signature}, {hash},
                    {emptyJson}::jsonb, true, false, now())
            """);

        var postgres = (await act.Should().ThrowAsync<PostgresException>()).Which;

        postgres.SqlState.Should().Be("23505");
        postgres.ConstraintName.Should().Be("ux_variants_product_signature");
    }

    [Fact]
    public async Task Product_Roundtrips_Attribute_Values_As_Jsonb()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fixtures = await SeedAsync(factory.Services, suffix);

        var product = Draft(fixtures, "Tee", $"jsonb-{suffix}");
        var variant = product.AddVariant(
            NextId(), $"SKJ{suffix}", new Dictionary<string, string> { ["Size"] = "XL" },
            DateTimeOffset.UtcNow);
        await SaveAsync(factory.Services, product);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        // Read as jsonb, not as text: phase 3's storefront views index into this column, and a
        // string column holding JSON would serve every one of those queries wrongly.
        var size = await db.Database
            .SqlQuery<string>($"SELECT attribute_values->>'size' AS \"Value\" FROM catalog.variants WHERE id = {variant.Id}")
            .SingleAsync();

        size.Should().Be("XL");
    }

    [Fact]
    public async Task A_Status_Outside_The_Three_Is_Rejected_By_The_Database()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fixtures = await SeedAsync(factory.Services, suffix);

        var product = Draft(fixtures, "Kettle", $"status-{suffix}");
        await SaveAsync(factory.Services, product);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        var act = async () => await db.Database.ExecuteSqlAsync(
            $"UPDATE catalog.products SET status = 'Published' WHERE id = {product.Id}");

        var postgres = (await act.Should().ThrowAsync<PostgresException>()).Which;

        postgres.SqlState.Should().Be("23514");
        postgres.ConstraintName.Should().Be("ck_products_status");
    }
}
