using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Catalog;

/// <summary>
/// What the catalogue's own database guarantees, as opposed to what its domain checks. These
/// matter because an import writes many rows at once and a fix-up script writes them directly:
/// the aggregate only guards the path through the application.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class CatalogConstraintTests(PostgresFixture fixture)
{
    private static long NextId() => Random.Shared.NextInt64(800_000_000, 899_999_999);

    private static Category Root(string name, string slug) =>
        Category.Create(NextId(), name, slug, null, null, 0, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Root_Categories_With_The_Same_Canonical_Key_Are_Rejected()
    {
        using var factory = fixture.CreateFactory();

        var suffix = Guid.NewGuid().ToString("N")[..8];

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
            db.Set<Category>().Add(Root($"Garden Tools {suffix}", $"garden-tools-{suffix}"));
            await db.SaveChangesAsync();
        }

        // Same name with different spacing — one category to anyone reading the catalogue, and
        // the canonical key is what makes the database agree.
        var act = async () =>
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
            db.Set<Category>().Add(Root($"Garden  Tools  {suffix}", $"garden-tools-{suffix}-2"));
            await db.SaveChangesAsync();
        };

        var postgres = (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<PostgresException>().Which;

        postgres.SqlState.Should().Be("23505");
        postgres.ConstraintName.Should().Be("ux_categories_parent_canonical_key",
            "both rows have a null parent_id, so this only holds because the index is "
            + "NULLS NOT DISTINCT — the Postgres default would treat the two roots as different");
    }

    [Fact]
    public async Task The_Same_Canonical_Key_Under_Different_Parents_Is_Allowed()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        var first = Root($"Parent A {suffix}", $"parent-a-{suffix}");
        var second = Root($"Parent B {suffix}", $"parent-b-{suffix}");
        db.Set<Category>().AddRange(first, second);
        await db.SaveChangesAsync();

        db.Set<Category>().Add(Category.Create(
            NextId(), $"Accessories {suffix}", $"acc-a-{suffix}", null, first.Id, 0, DateTimeOffset.UtcNow));
        db.Set<Category>().Add(Category.Create(
            NextId(), $"Accessories {suffix}", $"acc-b-{suffix}", null, second.Id, 0, DateTimeOffset.UtcNow));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync(
            "uniqueness is per parent: two branches may each have an 'Accessories'");
    }

    [Fact]
    public async Task Aliases_Round_Trip_Through_The_Text_Array_Column()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        long id;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
            var category = Root($"Kettles {suffix}", $"kettles-{suffix}");
            category.SetAliases(["Electric Kettles", "Tea Makers"], DateTimeOffset.UtcNow);

            db.Set<Category>().Add(category);
            await db.SaveChangesAsync();
            id = category.Id;
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

            var reloaded = await db.Set<Category>().AsNoTracking()
                .SingleAsync(category => category.Id == id);

            reloaded.Aliases.Should().BeEquivalentTo(["electric-kettles", "tea-makers"]);
            reloaded.CanonicalKey.Should().Be($"kettles-{suffix}");
        }
    }

    [Fact]
    public async Task An_Alias_Lookup_Uses_Array_Containment()
    {
        using var factory = fixture.CreateFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        var category = Root($"Kettles {suffix}", $"kettles-{suffix}");
        category.SetAliases([$"Boilers {suffix}"], DateTimeOffset.UtcNow);
        db.Set<Category>().Add(category);
        await db.SaveChangesAsync();

        // The shape the GIN index serves: "which category answers to this name".
        var found = await db.Set<Category>().AsNoTracking()
            .Where(c => c.Aliases.Contains($"boilers-{suffix}"))
            .SingleOrDefaultAsync();

        found.Should().NotBeNull();
        found!.Id.Should().Be(category.Id);
    }
}
