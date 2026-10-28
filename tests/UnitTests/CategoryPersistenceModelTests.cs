using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Reflection;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class CategoryPersistenceModelTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Model_uses_catalog_schema_and_singular_category_table_names()
    {
        var options = new DbContextOptionsBuilder<EwaysVNextDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=CategoryModel;Trusted_Connection=True")
            .Options;
        using var db = new EwaysVNextDbContext(options);

        var category = db.Model.FindEntityType(typeof(Category))!;
        var assignment = db.Model.FindEntityType(typeof(CategoryProduct))!;

        category.GetSchema().Should().Be("catalog");
        category.GetTableName().Should().Be("Category");
        assignment.GetSchema().Should().Be("catalog");
        assignment.GetTableName().Should().Be("CategoryProduct");
    }

    [Fact]
    public void SqlServer_Model_Must_Map_Long_Product_Ids_As_Bigint()
    {
        var options = new DbContextOptionsBuilder<EwaysVNextDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=CategoryModel;Trusted_Connection=True")
            .Options;
        using var db = new EwaysVNextDbContext(options);

        var assignment = db.Model.FindEntityType(typeof(CategoryProduct))!;

        assignment.FindProperty(nameof(CategoryProduct.ProductId))!
            .GetColumnType().Should().Be("bigint");
    }

    [Fact]
    public void Model_Must_Convert_Category_Slug_To_String()
    {
        var options = new DbContextOptionsBuilder<EwaysVNextDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=CategoryModel;Trusted_Connection=True")
            .Options;
        using var db = new EwaysVNextDbContext(options);

        var slug = db.Model.FindEntityType(typeof(Category))!
            .FindProperty(nameof(Category.Slug))!;

        var converter = slug.GetValueConverter();
        converter.Should().NotBeNull();
        converter!.ModelClrType.Should().Be<CategorySlug>();
        converter.ProviderClrType.Should().Be<string>();
    }

    [Fact]
    public void Model_Must_Use_Client_Generated_Ids_And_Explicit_Self_Navigations()
    {
        var options = new DbContextOptionsBuilder<EwaysVNextDbContext>()
            .UseInMemoryDatabase(nameof(Model_Must_Use_Client_Generated_Ids_And_Explicit_Self_Navigations))
            .Options;
        using var db = new EwaysVNextDbContext(options);
        var entity = db.Model.FindEntityType(typeof(Category))!;

        entity.FindProperty(nameof(Category.Id))!.ValueGenerated
            .Should().Be(ValueGenerated.Never);
        entity.FindProperty(nameof(Category.Name))!.GetMaxLength()
            .Should().Be(60);
        entity.FindProperty(nameof(Category.Status)).Should().NotBeNull();
        entity.FindProperty(nameof(Category.Status))!.ClrType.Should().Be<CategoryStatus>();
        entity.FindProperty("IsActive").Should().BeNull();
        entity.FindProperty("IsDeleted").Should().BeNull();
        var hierarchyIndex = entity.GetIndexes().Single(index =>
            index.GetDatabaseName() == "IX_Category_Hierarchy");
        hierarchyIndex.Properties.Select(property => property.Name).Should().Equal(
            nameof(Category.ParentId), nameof(Category.Status),
            nameof(Category.DisplayOrder), nameof(Category.Id));
        var siblingNameIndex = entity.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Category.ParentId), nameof(Category.NormalizedName)]));
        siblingNameIndex.IsUnique.Should().BeTrue();
        siblingNameIndex.GetFilter().Should().Be("[Status] <> 2");
        entity.GetIndexes().Single(index => index.GetDatabaseName() == "IX_Category_Slug")
            .GetFilter().Should().Be("[Status] <> 2");
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
        var options = new DbContextOptionsBuilder<EwaysVNextDbContext>()
            .UseInMemoryDatabase(nameof(Parent_And_Children_Navigations_Must_Persist_And_Reload))
            .Options;
        await using var db = new EwaysVNextDbContext(options);
        var parent = CreateWithId(912345678901L, null, "Parent", "parent");
        var child = CreateWithId(912345678902L, parent.Id, "Child", "child");
        await db.Categories.AddRangeAsync(parent, child);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloadedParent = await db.Categories
            .Include("Children")
            .SingleAsync(category => category.Id == parent.Id);
        var reloadedChild = await db.Categories
            .Include("Parent")
            .SingleAsync(category => category.Id == child.Id);

        var children = (IReadOnlyCollection<Category>)typeof(Category)
            .GetProperty("Children")!.GetValue(reloadedParent)!;
        var reloadedParentReference = (Category?)typeof(Category)
            .GetProperty("Parent")!.GetValue(reloadedChild);

        children.Should().ContainSingle()
            .Which.Id.Should().Be(child.Id);
        reloadedParentReference.Should().NotBeNull();
        reloadedParentReference!.Id.Should().Be(parent.Id);
    }

    [Fact]
    public void UnifyCategoryStatus_Migration_Must_Backfill_Both_Directions_Before_Dropping_Source_Columns()
    {
        var migrationType = typeof(EwaysVNextDbContext).Assembly.GetType(
            "EwaysVNext.Database.Migrations.UnifyCategoryStatus");
        migrationType.Should().NotBeNull();
        var migration = (Migration)Activator.CreateInstance(migrationType!)!;

        var up = BuildOperations(migration, "Up");
        var addStatus = IndexOf<AddColumnOperation>(up, operation => operation.Name == "Status");
        var upBackfill = IndexOf<SqlOperation>(up, operation =>
            operation.Sql.Contains(
                "CASE WHEN [IsDeleted] = 1 THEN 2 WHEN [IsActive] = 1 THEN 1 ELSE 0 END",
                StringComparison.Ordinal));
        var dropIsActive = IndexOf<DropColumnOperation>(up, operation => operation.Name == "IsActive");
        var dropIsDeleted = IndexOf<DropColumnOperation>(up, operation => operation.Name == "IsDeleted");

        addStatus.Should().BeLessThan(upBackfill);
        upBackfill.Should().BeLessThan(dropIsActive);
        upBackfill.Should().BeLessThan(dropIsDeleted);

        var down = BuildOperations(migration, "Down");
        var addIsActive = IndexOf<AddColumnOperation>(down, operation => operation.Name == "IsActive");
        var addIsDeleted = IndexOf<AddColumnOperation>(down, operation => operation.Name == "IsDeleted");
        var downBackfill = IndexOf<SqlOperation>(down, operation =>
            operation.Sql.Contains(
                "[IsActive] = CASE WHEN [Status] = 1 THEN 1 ELSE 0 END",
                StringComparison.Ordinal)
            && operation.Sql.Contains(
                "[IsDeleted] = CASE WHEN [Status] = 2 THEN 1 ELSE 0 END",
                StringComparison.Ordinal));
        var dropStatus = IndexOf<DropColumnOperation>(down, operation => operation.Name == "Status");

        addIsActive.Should().BeLessThan(downBackfill);
        addIsDeleted.Should().BeLessThan(downBackfill);
        downBackfill.Should().BeLessThan(dropStatus);
    }

    private static List<MigrationOperation> BuildOperations(Migration migration, string methodName)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    private static int IndexOf<TOperation>(
        List<MigrationOperation> operations,
        Func<TOperation, bool> predicate)
        where TOperation : MigrationOperation
    {
        for (var index = 0; index < operations.Count; index++)
        {
            if (operations[index] is TOperation operation && predicate(operation))
                return index;
        }

        throw new InvalidOperationException($"Expected migration operation {typeof(TOperation).Name} was not found.");
    }

    private static Category CreateWithId(
        long id, long? parentId, string name, string slug)
    {
        return Category.Create(id, name, slug, null, parentId, 0, CreatedAt);
    }
}
