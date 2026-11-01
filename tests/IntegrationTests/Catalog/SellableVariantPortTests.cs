using AwesomeAssertions;
using EwaysVNext.Core.Application;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Commands;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Catalog;

/// <summary>
/// What the catalogue tells the rest of the system is buyable.
/// <para>
/// Built through <see cref="ICatalogAdminService"/> rather than by inserting rows, so the data
/// these assertions run against is the data an admin would actually have produced.
/// </para>
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class SellableVariantPortTests(PostgresFixture fixture)
{
    private static readonly AdminActor Actor = new(Guid.NewGuid(), IsSuperAdmin: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record World(long BrandId, long ParentCategoryId, long CategoryId);

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<T> InScopeAsync<T>(
        WebApplicationFactory<Program> factory, Func<IServiceProvider, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>A brand and a two-level category tree: "Kitchen {suffix}" → "Kettles {suffix}".</summary>
    private static async Task<World> SeedAsync(WebApplicationFactory<Program> factory, string suffix) =>
        await InScopeAsync(factory, async services =>
        {
            var brands = services.GetRequiredService<IBrandAdminService>();
            var categories = services.GetRequiredService<ICategoryService>();

            var brandId = (await brands.CreateAsync($"Brand {suffix}", [], Actor, Ct)).Value;

            var parentId = (await categories.CreateAsync(
                new CreateCategoryCommand($"Kitchen {suffix}", $"kitchen-{suffix}", null, null, 0), Ct)).Value;
            var categoryId = (await categories.CreateAsync(
                new CreateCategoryCommand($"Kettles {suffix}", $"kettles-{suffix}", null, parentId, 0), Ct)).Value;

            return new World(brandId, parentId, categoryId);
        });

    /// <summary>A product with one variant and one usable image, left in Draft.</summary>
    private static async Task<(long ProductId, long VariantId)> DraftProductAsync(
        WebApplicationFactory<Program> factory, World world, string suffix) =>
        await InScopeAsync(factory, async services =>
        {
            var admin = services.GetRequiredService<ICatalogAdminService>();

            var productId = (await admin.CreateProductAsync(
                new CreateProductCommand(
                    world.BrandId, world.CategoryId, $"Demo Kettle {suffix}", null, null, null),
                Actor, Ct)).Value;

            var variantId = (await admin.AddVariantAsync(
                new AddVariantCommand(productId, $"SKU-{suffix}", new Dictionary<string, string> { ["Size"] = "1.7L" }),
                Actor, Ct)).Value;

            using var image = new MemoryStream(
                [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52]);
            (await admin.UploadMediaAsync(
                new UploadMediaCommand(productId, image, image.Length, "kettle", MediaUsageRights.Allowed, null),
                Actor, Ct)).IsSuccess.Should().BeTrue();

            return (productId, variantId);
        });

    private static async Task<(long ProductId, long VariantId)> ActiveProductAsync(
        WebApplicationFactory<Program> factory, World world, string suffix)
    {
        var (productId, variantId) = await DraftProductAsync(factory, world, suffix);

        await InScopeAsync(factory, async services =>
        {
            var admin = services.GetRequiredService<ICatalogAdminService>();
            var activated = await admin.SetProductActiveAsync(productId, true, Actor, Ct);
            activated.IsSuccess.Should().BeTrue(activated.Error?.Message);
            return true;
        });

        return (productId, variantId);
    }

    private static Task<IReadOnlyList<SellableVariant>> SellableAsync(
        WebApplicationFactory<Program> factory, params long[] variantIds) =>
        InScopeAsync(factory, services =>
            services.GetRequiredService<ISellableVariantPort>()
                .GetSellableAsync(variantIds.Length == 0 ? null : variantIds, Ct));

    [Fact]
    public async Task Draft_Product_Variants_Are_Not_Sellable()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Suffix();
        var world = await SeedAsync(factory, suffix);
        var (_, variantId) = await DraftProductAsync(factory, world, suffix);

        (await SellableAsync(factory, variantId)).Should().BeEmpty(
            "a draft has never been offered for sale");
    }

    [Fact]
    public async Task An_Active_Product_Variant_Is_Sellable()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Suffix();
        var world = await SeedAsync(factory, suffix);
        var (productId, variantId) = await ActiveProductAsync(factory, world, suffix);

        var sellable = await SellableAsync(factory, variantId);

        sellable.Should().ContainSingle();
        sellable[0].ProductId.Should().Be(productId);
        sellable[0].Attributes["size"].Should().Be("1.7L");
    }

    [Fact]
    public async Task Admin_Deactivated_Variant_Is_Not_Sellable()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Suffix();
        var world = await SeedAsync(factory, suffix);
        var (productId, variantId) = await ActiveProductAsync(factory, world, suffix);

        await InScopeAsync(factory, async services =>
            await services.GetRequiredService<ICatalogAdminService>()
                .SetVariantActiveAsync(productId, variantId, false, Actor, Ct));

        (await SellableAsync(factory, variantId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Inactive_Brand_Makes_Variants_Unsellable()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Suffix();
        var world = await SeedAsync(factory, suffix);
        var (_, variantId) = await ActiveProductAsync(factory, world, suffix);

        (await SellableAsync(factory, variantId)).Should().ContainSingle();

        await InScopeAsync(factory, async services =>
            await services.GetRequiredService<IBrandAdminService>()
                .SetActiveAsync(world.BrandId, false, Actor, Ct));

        (await SellableAsync(factory, variantId)).Should().BeEmpty(
            "switching a brand off takes its whole catalogue off sale, without touching a product");
    }

    [Fact]
    public async Task Sellable_Variant_Has_Category_Path_Root_To_Leaf()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Suffix();
        var world = await SeedAsync(factory, suffix);
        var (_, variantId) = await ActiveProductAsync(factory, world, suffix);

        var sellable = await SellableAsync(factory, variantId);

        sellable[0].CategoryPath.Should().Equal($"Kitchen {suffix}", $"Kettles {suffix}");
    }

    [Fact]
    public async Task Null_Filter_Returns_All_Sellable()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Suffix();
        var world = await SeedAsync(factory, suffix);
        var (_, first) = await ActiveProductAsync(factory, world, $"{suffix}a");
        var (_, second) = await ActiveProductAsync(factory, world, $"{suffix}b");

        var all = await SellableAsync(factory);

        all.Select(variant => variant.VariantId).Should().Contain([first, second]);
    }

    [Fact]
    public async Task Lookup_Reports_Existing_Draft_Variant_As_Exists_But_Not_Sellable()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Suffix();
        var world = await SeedAsync(factory, suffix);
        var (productId, variantId) = await DraftProductAsync(factory, world, suffix);

        var states = await InScopeAsync(factory, services =>
            services.GetRequiredService<IVariantLookupPort>().GetStatesAsync([variantId, 999_999_999L], Ct));

        states[variantId].Should().Be(new VariantState(variantId, productId, true, false),
            "pricing a draft variant is how a product is made ready before it goes live");
        states[999_999_999L].Exists.Should().BeFalse(
            "every id asked about gets an answer; a missing key would read as 'I forgot to ask'");
    }

    [Fact]
    public async Task Version_Increases_When_A_Variant_Is_Deactivated()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Suffix();
        var world = await SeedAsync(factory, suffix);
        var (productId, variantId) = await ActiveProductAsync(factory, world, suffix);

        var before = await InScopeAsync(factory, services =>
            services.GetRequiredService<ISellableVariantPort>().GetVersionAsync(Ct));

        await InScopeAsync(factory, async services =>
            await services.GetRequiredService<ICatalogAdminService>()
                .SetVariantActiveAsync(productId, variantId, false, Actor, Ct));

        var after = await InScopeAsync(factory, services =>
            services.GetRequiredService<ISellableVariantPort>().GetVersionAsync(Ct));

        after.Should().BeGreaterThan(before,
            "the port reports the same number the change notifier bumps, so a holder of the old "
            + "one knows its copy is stale");
    }

    [Fact]
    public async Task Sellability_Version_Increases_After_Notification()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ICatalogChangeNotifier>();

        var before = await notifier.CurrentVersionAsync(Ct);
        await notifier.SellabilityChangedAsync(Ct);
        var after = await notifier.CurrentVersionAsync(Ct);

        after.Should().BeGreaterThan(before,
            "a cart compares this number to decide whether its contents went stale");
    }

    [Fact]
    public async Task A_Display_Change_Leaves_The_Version_Alone()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ICatalogChangeNotifier>();

        var before = await notifier.CurrentVersionAsync(Ct);
        await notifier.DisplayChangedAsync(Ct);
        var after = await notifier.CurrentVersionAsync(Ct);

        after.Should().Be(before,
            "a retitled product or a new image changes nothing about what a cart holds, and "
            + "bumping the version would invalidate every cart in the system over a typo");
    }

    [Fact]
    public async Task The_Application_Role_May_Advance_The_Sequence()
    {
        // Tests connect as eways_app, which owns nothing. If the default privileges in
        // 01-roles.sql ever stop covering sequences, this is where it shows.
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ICatalogChangeNotifier>();

        var act = async () => await notifier.CurrentVersionAsync(Ct);

        await act.Should().NotThrowAsync();
    }
}
