using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace EwaysVNext.PersistenceContractTests;

/// <summary>
/// The Category mapping contract. Names are asserted in the snake_case the Postgres convention
/// produces (ADR-0017); filters are asserted in Postgres syntax, because a filter is raw SQL the
/// provider does not translate — a SQL Server filter would reach the database verbatim and fail.
/// </summary>
public sealed class CategoryPersistenceModelTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Model_Uses_Catalog_Schema_And_Snake_Case_Category_Table_Names()
    {
        using var db = Providers.Npgsql<EwaysVNextDbContext>();

        var category = db.Model.FindEntityType(typeof(Category))!;
        var assignment = db.Model.FindEntityType(typeof(CategoryProduct))!;

        category.GetSchema().Should().Be("catalog");
        category.GetTableName().Should().Be("categories");
        assignment.GetSchema().Should().Be("catalog");
        assignment.GetTableName().Should().Be("category_products");
    }

    [Fact]
    public void Model_Must_Map_Long_Product_Ids_As_Bigint()
    {
        using var db = Providers.Npgsql<EwaysVNextDbContext>();

        db.Model.FindEntityType(typeof(CategoryProduct))!
            .FindProperty(nameof(CategoryProduct.ProductId))!
            .GetColumnType().Should().Be("bigint");
    }

    [Fact]
    public void Model_Must_Convert_Category_Slug_To_String()
    {
        using var db = Providers.Npgsql<EwaysVNextDbContext>();

        var converter = db.Model.FindEntityType(typeof(Category))!
            .FindProperty(nameof(Category.Slug))!.GetValueConverter();

        converter.Should().NotBeNull();
        converter!.ModelClrType.Should().Be<CategorySlug>();
        converter.ProviderClrType.Should().Be<string>();
    }

    [Fact]
    public void Model_Must_Use_Client_Generated_Ids_And_Explicit_Self_Navigations()
    {
        using var db = Providers.Npgsql<EwaysVNextDbContext>();
        var entity = db.Model.FindEntityType(typeof(Category))!;

        entity.FindProperty(nameof(Category.Id))!.ValueGenerated.Should().Be(ValueGenerated.Never);
        entity.FindProperty(nameof(Category.Name))!.GetMaxLength().Should().Be(60);
        entity.FindProperty(nameof(Category.Status)).Should().NotBeNull();
        entity.FindProperty(nameof(Category.Status))!.ClrType.Should().Be<CategoryStatus>();
        entity.FindProperty("IsActive").Should().BeNull();
        entity.FindProperty("IsDeleted").Should().BeNull();

        var hierarchyIndex = entity.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(Category.ParentId), nameof(Category.Status),
                 nameof(Category.DisplayOrder), nameof(Category.Id)]));
        hierarchyIndex.IsUnique.Should().BeFalse();

        // Siblings are unique by canonical key, not by the display name: "Kitchen & Dining" and
        // "kitchen  dining" are the same category to an import, and the key is what an import
        // matches on.
        var siblingKeyIndex = entity.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Category.ParentId), nameof(Category.CanonicalKey)]));
        siblingKeyIndex.IsUnique.Should().BeTrue();
        siblingKeyIndex.GetFilter().Should().Be("status <> 2");

        // The NULLS NOT DISTINCT part is not visible on the model — Npgsql emits it at SQL
        // generation — so it is proved against a real database by
        // CatalogConstraintTests.Root_Categories_With_The_Same_Canonical_Key_Are_Rejected.

        var aliasIndex = entity.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(["Aliases"]));
        aliasIndex.GetMethod().Should().Be("gin",
            "the lookup asks which category holds an alias, which a btree cannot answer");

        entity.GetIndexes()
            .Single(index => index.Properties.Select(p => p.Name).SequenceEqual([nameof(Category.Slug)]))
            .GetFilter().Should().Be("status <> 2");

        var parent = entity.FindNavigation("Parent");
        var children = entity.FindNavigation("Children");
        parent.Should().NotBeNull();
        children.Should().NotBeNull();
        parent!.ForeignKey.Should().BeSameAs(children!.ForeignKey);
        parent.ForeignKey.IsRequired.Should().BeFalse();
        parent.ForeignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    [Fact]
    public async Task Parent_And_Children_Navigations_Must_Persist_And_Reload()
    {
        await using var db = Providers.InMemory<EwaysVNextDbContext>();
        var parent = CreateWithId(912345678901L, null, "Parent", "parent");
        var child = CreateWithId(912345678902L, parent.Id, "Child", "child");
        await db.Set<Category>().AddRangeAsync(parent, child);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloadedParent = await db.Set<Category>()
            .Include("Children").SingleAsync(category => category.Id == parent.Id);
        var reloadedChild = await db.Set<Category>()
            .Include("Parent").SingleAsync(category => category.Id == child.Id);

        var children = (IReadOnlyCollection<Category>)typeof(Category)
            .GetProperty("Children")!.GetValue(reloadedParent)!;
        var reloadedParentReference = (Category?)typeof(Category)
            .GetProperty("Parent")!.GetValue(reloadedChild);

        children.Should().ContainSingle().Which.Id.Should().Be(child.Id);
        reloadedParentReference.Should().NotBeNull();
        reloadedParentReference!.Id.Should().Be(parent.Id);
    }

    private static Category CreateWithId(long id, long? parentId, string name, string slug) =>
        Category.Create(id, name, slug, null, parentId, 0, CreatedAt);
}
